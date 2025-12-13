using Ecierge.Eveneum;
using Eveneum.Documents;
using Eveneum.StoredProcedures;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Eveneum.Persistence;

public class CosmosPersistence : ICosmosPersistence
{
    protected readonly Container Container;
    protected readonly BulkDeleteMode BulkDeleteMode;

    private const string BulkDeleteStoredProc = "Eveneum.BulkDelete";

    public CosmosPersistence(CosmosClient cosmosClient, string databaseName, string containerName, BulkDeleteMode bulkDeleteMode = BulkDeleteMode.StoredProcedure)
    {
        if (cosmosClient is null)
            throw new ArgumentNullException(nameof(cosmosClient));

        if (string.IsNullOrEmpty(databaseName))
            throw new ArgumentNullException(nameof(databaseName));

        if (string.IsNullOrEmpty(containerName))
            throw new ArgumentNullException(nameof(containerName));

        var database = cosmosClient.GetDatabase(databaseName);
        this.Container = database.GetContainer(containerName);

        this.BulkDeleteMode = bulkDeleteMode;
    }

    public virtual async Task Initialize(CancellationToken cancellationToken = default)
    {
        if (this.BulkDeleteMode == BulkDeleteMode.StoredProcedure)
            await CreateOrUpdateStoredProcedureAsync(BulkDeleteStoredProc, "BulkDelete", cancellationToken);
    }

    public async Task CreateOrUpdateStoredProcedureAsync(
        string procedureId,
        string procedureFileName,
        CancellationToken cancellationToken = default)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            typeof(EventStore),
            $"StoredProcedures.{procedureFileName}.js");
        
        if (stream is null)
            throw new InvalidOperationException($"Could not find embedded resource for stored procedure: {procedureFileName}");

        using var reader = new StreamReader(stream);

        var properties = new StoredProcedureProperties
        {
            Id = procedureId,
            Body = await reader.ReadToEndAsync()
        };

        try
        {
            await Container.Scripts.ReadStoredProcedureAsync(procedureId, cancellationToken: cancellationToken);
            await Container.Scripts.ReplaceStoredProcedureAsync(properties, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await Container.Scripts.CreateStoredProcedureAsync(properties, cancellationToken: cancellationToken);
        }
    }

    public virtual async Task<CosmosItemResponse<EveneumDocument?>> UpsertItemAsync(
        EveneumDocument document,
        PartitionKey partitionKey,
        CancellationToken cancellationToken = default)
    {
        var result = await Container.UpsertItemAsync(
            document,
            partitionKey,
            cancellationToken: cancellationToken);

        return new CosmosItemResponse<EveneumDocument?>(result.Resource, result.RequestCharge);
    }

    public virtual async Task<CosmosItemResponse<EveneumDocument?>> ReplaceItemAsync(
        EveneumDocument document,
        string id,
        PartitionKey partitionKey,
        CancellationToken cancellationToken = default)
    {
        var result = await Container.ReplaceItemAsync(
            document,
            id,
            partitionKey,
            cancellationToken: cancellationToken);

        return new CosmosItemResponse<EveneumDocument?>(result.Resource, result.RequestCharge);
    }

    public virtual async Task<StoredProcedureExecuteResponse<T>> ExecuteStoredProcedureAsync<T>(
        string storedProcedureId,
        PartitionKey partitionKey,
        object?[] parameters,
        CancellationToken cancellationToken = default)
    {
        return await Container.Scripts.ExecuteStoredProcedureAsync<T>(
            storedProcedureId,
            partitionKey,
            parameters,
            cancellationToken: cancellationToken);
    }

    public virtual TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey)
    {
        return Container.CreateTransactionalBatch(partitionKey);
    }

    public virtual ICosmosFeedIterator<EveneumDocument> GetItemQueryIterator(string queryText, PartitionKey partitionKey, int? maxItemCount = null)
    {
        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = partitionKey,
            MaxItemCount = maxItemCount
        };

        return new CosmosFeedIterator<EveneumDocument, EveneumDocument>(Container.GetItemQueryIterator<EveneumDocument>(queryText, requestOptions: requestOptions));
    }

    public virtual ICosmosFeedIterator<EveneumDocument> GetItemQueryIterator(QueryDefinition queryDefinition, int? maxItemCount = null)
    {
        var requestOptions = new QueryRequestOptions
        {
            MaxItemCount = maxItemCount
        };

        return new CosmosFeedIterator<EveneumDocument, EveneumDocument>(Container.GetItemQueryIterator<EveneumDocument>(queryDefinition, requestOptions: requestOptions));
    }

    public virtual async Task<CosmosItemResponse<EveneumDocument>> ReadItemAsync(string id, PartitionKey partitionKey, CancellationToken cancellationToken = default)
    {
        var result = await Container.ReadItemAsync<EveneumDocument>(
            id,
            partitionKey,
            cancellationToken: cancellationToken);

        return new CosmosItemResponse<EveneumDocument>(result.Resource, result.RequestCharge);
    }

    public Task<DeleteResponse> DeleteItems(StreamId streamId, string query, bool softDelete, int? ttl, byte batchSize, int? maxItemCount = null, CancellationToken cancellationToken = default) =>
        this.BulkDeleteMode == BulkDeleteMode.TransactionalBatch
            ? this.BulkDeleteDocumentsUsingTransactionalBatch(streamId, query, softDelete, ttl, batchSize, maxItemCount, cancellationToken)
            : this.BulkDeleteDocumentsUsingStoredProcedure(streamId, query, softDelete, ttl, cancellationToken);

    private async Task<DeleteResponse> BulkDeleteDocumentsUsingStoredProcedure(StreamId streamId, string query, bool softDelete, int? ttl, CancellationToken cancellationToken = default)
    {
        double requestCharge = 0;
        ulong deletedDocuments = 0;
        StoredProcedureExecuteResponse<BulkDeleteResponse> response;

        do
        {
            response = await this.ExecuteStoredProcedureAsync<BulkDeleteResponse>(
                BulkDeleteStoredProc,
                streamId.ToPartitionKey(),
                [query, softDelete, ttl],
                cancellationToken);

            requestCharge += response.RequestCharge;
            deletedDocuments += response.Resource.Deleted;
        }
        while (response.Resource.Continuation);

        return new DeleteResponse(deletedDocuments, requestCharge);
    }

    private async Task<DeleteResponse> BulkDeleteDocumentsUsingTransactionalBatch(StreamId streamId, string query, bool softDelete, int? ttl, byte batchSize, int? maxItemCount = null, CancellationToken cancellationToken = default)
    {
        double requestCharge = 0;
        ulong deletedDocuments = 0;
        List<EveneumDocument> documents;

        do
        {
            documents = [];

            using (var iterator = this.GetItemQueryIterator(query, streamId.ToPartitionKey(), maxItemCount))
            {
                while (iterator.HasMoreResults && documents.Count == 0)
                {
                    var page = await iterator.ReadNextAsync(cancellationToken);

                    requestCharge += page.RequestCharge;
                    documents.AddRange(page);
                }
            }

            foreach (var batch in documents.Batch(batchSize))
            {
                var transaction = this.CreateTransactionalBatch(streamId.ToPartitionKey());

                foreach (var document in batch)
                {
                    if (softDelete)
                    {
                        document.Deleted = true;

                        if (ttl > 0)
                            document.TimeToLive = ttl;

                        transaction.ReplaceItem(document.Id, document, new TransactionalBatchItemRequestOptions { IfMatchEtag = document.ETag });
                    }
                    else
                        transaction.DeleteItem(document.Id, new TransactionalBatchItemRequestOptions { IfMatchEtag = document.ETag });
                }

                using var response = await transaction.ExecuteAsync(cancellationToken);
                requestCharge += response.RequestCharge;

                if (!response.IsSuccessStatusCode)
                    throw new WriteException(streamId.LogicalStreamId, requestCharge, response.ErrorMessage, response.StatusCode);

                for (var i = 0; i < batch.Count(); i++)
                {
                    var operationResult = response.GetOperationResultAtIndex<EveneumDocument>(i);

                    if (operationResult.IsSuccessStatusCode)
                        deletedDocuments++;
                    else
                        throw new WriteException(streamId.LogicalStreamId, requestCharge, $"deletion of document {batch.ElementAt(i).Id} failed", operationResult.StatusCode);
                }
            }
        }
        while (documents.Count > 0);

        return new DeleteResponse(deletedDocuments, requestCharge);
    }
}

using Ecierge.Eveneum;
using Eveneum.Advanced;
using Eveneum.Documents;
using Eveneum.Persistence;
using Eveneum.Serialization;
using Eveneum.Snapshots;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Eveneum;

public class EventStore : IEventStore, IAdvancedEventStore
{
    private readonly ICosmosPersistence Persistence;
    private readonly Action<StreamId, IDictionary<string, object?>>? streamIdJsonMapping;

    public DeleteMode DeleteMode { get; }
    public TimeSpan StreamTimeToLiveAfterDelete { get; }
    public TimeSpan DraftEventTimeToLive { get; }
    public byte BatchSize { get; }
    public int QueryMaxItemCount { get; }
    public EveneumDocumentSerializer Serializer { get; }
    public System.Text.Json.JsonSerializerOptions JsonSerializerOptions => this.Serializer.JsonSerializerOptions;
    public ISnapshotWriter? SnapshotWriter { get; }
    public SnapshotMode SnapshotMode { get; }

    public EventStore(
        CosmosClient client,
        string database,
        string container,
        EventStoreOptions? options = null)
        : this(new CosmosPersistence(client, database, container), options)
    {
    }

    public EventStore(ICosmosPersistence persistence, EventStoreOptions? options = null)
    {
        Persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        options = options ?? new EventStoreOptions();

        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), options.BatchSize, "BatchSize must be greater than zero.");

        this.streamIdJsonMapping = options.StreamIdJsonMapping;

        this.DeleteMode = options.DeleteMode;
        this.StreamTimeToLiveAfterDelete = options.StreamTimeToLiveAfterDelete;
        this.DraftEventTimeToLive = options.DraftEventTimeToLive;
        this.BatchSize = Math.Min(options.BatchSize, (byte)100); // Maximum batch size supported by CosmosDB
        this.QueryMaxItemCount = options.QueryMaxItemCount;
        this.Serializer = new EveneumDocumentSerializer(options.JsonSerializerOptions);
        this.SnapshotWriter = options.SnapshotWriter;
        this.SnapshotMode = options.SnapshotMode;
    }

    private string GetJsonPropertyName(string clrName)
    {
        var prop = typeof(EveneumDocument).GetProperty(clrName);
        if (prop != null)
        {
            var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attr != null)
                return attr.Name;
        }

        var policy = Serializer.JsonSerializerOptions.PropertyNamingPolicy;
        return policy?.ConvertName(clrName) ?? clrName;
    }

    private void ApplyStreamIdJson(StreamId streamId, EveneumDocument doc) =>
        streamIdJsonMapping?.Invoke(streamId, doc.CustomJsonProperties);

    public async Task Initialize(CancellationToken cancellationToken = default) =>
        await Persistence.Initialize(cancellationToken);

    public Task<StreamResponse> ReadStream(StreamId streamId, ReadStreamOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = options ?? new ReadStreamOptions();

        var maxItemCount = options.MaxItemCount ?? QueryMaxItemCount;
        var documentType = GetJsonPropertyName(nameof(EveneumDocument.DocumentType));
        var documentVersion = GetJsonPropertyName(nameof(EveneumDocument.Version));

        var whereTerms = new List<string>();

        if (options.IgnoreSnapshots)
            whereTerms.Add($"x.{documentType} <> '{nameof(DocumentType.Snapshot)}'");

        if (options.FromVersion.HasValue)
            whereTerms.Add($"(x.{documentVersion} >= {options.FromVersion.Value} OR x.{documentType} = '{nameof(DocumentType.Header)}')");

        if (options.ToVersion.HasValue)
            whereTerms.Add($"(x.{documentVersion} <= {options.ToVersion.Value} OR x.{documentType} = '{nameof(DocumentType.Header)}')");

        var selectClause = "SELECT * FROM x";
        var whereClause = whereTerms.Count > 0
            ? $"WHERE {string.Join(" AND ", whereTerms)}"
            : string.Empty;
        var orderByClause = $"ORDER BY x.{GetJsonPropertyName(nameof(EveneumDocument.SortOrder))} DESC";

        var query = $"{selectClause} {whereClause} {orderByClause}";

        return ReadStream(streamId, query, maxItemCount, cancellationToken);
    }

    private async Task<StreamResponse> ReadStream(StreamId streamId, string sql, int maxItemCount, CancellationToken cancellationToken)
    {
        using var iterator = this.Persistence.GetItemQueryIterator(sql, streamId.ToPartitionKey(), maxItemCount);

        var documents = new List<EveneumDocument>();
        var finishLoading = false;
        double requestCharge = 0;

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);

            requestCharge += page.RequestCharge;

            foreach (var eveneumDoc in page)
            {
                if (eveneumDoc.DocumentType == DocumentType.Header && eveneumDoc.Deleted)
                    return new StreamResponse(null, true, requestCharge);

                if (eveneumDoc.Deleted)
                    continue;

                documents.Add(eveneumDoc);

                if (eveneumDoc.DocumentType == DocumentType.Snapshot)
                {
                    finishLoading = true;
                    break;
                }
            }

            if (finishLoading)
                break;
        }

        if (documents.Count == 0)
            return new StreamResponse(null, false, requestCharge);

        var headerDocument = documents.FirstOrDefault(x => x.DocumentType == DocumentType.Header);

        if (headerDocument is null)
            throw new StreamNotFoundException(streamId.LogicalStreamId, requestCharge);

        try
        {
            var events = documents.Where(x => x.DocumentType == DocumentType.Event).Select(this.Serializer.DeserializeEvent).Reverse().ToArray();
            var metadata = headerDocument.Metadata;

            var snapshotDocument = documents.FirstOrDefault(x => x.DocumentType == DocumentType.Snapshot);

            Snapshot? snapshot = null;

            if(snapshotDocument is not null)
            {
                snapshot = this.Serializer.DeserializeSnapshot(snapshotDocument);

                var snapshotWriterTypePropName = this.Serializer.JsonSerializerOptions.PropertyNamingPolicy
                    ?.ConvertName(nameof(SnapshotWriterSnapshot.SnapshotWriterType))
                    ?? nameof(SnapshotWriterSnapshot.SnapshotWriterType);

                // Check if it's a SnapshotWriterSnapshot by looking at JSON structure
                if (snapshot.Value.Data.ValueKind == JsonValueKind.Object &&
                    snapshot.Value.Data.TryGetProperty(snapshotWriterTypePropName, out var writerTypeElement) &&
                    writerTypeElement.ValueKind == JsonValueKind.String &&
                    writerTypeElement.GetString() is { Length: > 0 } snapshotWriterType)
                {
                    if (this.SnapshotWriter is not null)
                        snapshot = await this.SnapshotWriter.ReadSnapshot(streamId, snapshot.Value.Version, cancellationToken);
                    else
                        throw new SnapshotWriterNotFoundException(streamId.LogicalStreamId, requestCharge, snapshotWriterType);
                }
            }

            return new StreamResponse(new Stream(streamId.LogicalStreamId, headerDocument.Version, metadata, events, snapshot), false, requestCharge);
        }
        catch (JsonDeserializationException ex)
        {
            throw new StreamDeserializationException(streamId.LogicalStreamId, requestCharge, ex.Type, ex);
        }
    }
    private string GetMetadataState (JsonElement element)
    {
        var metadata = element.GetProperty("state");
        if (metadata.ValueKind == JsonValueKind.String)
            return metadata.GetString() ?? string.Empty;
        return string.Empty;
    }

    public async Task<Response> WriteToStream(StreamId streamId, EventData[] events, ulong? expectedVersion = null, object? metadata = null, CancellationToken cancellationToken = default)
    {
        var timeToLive = DraftEventTimeToLive == TimeSpan.Zero ? null : (int?)DraftEventTimeToLive.TotalSeconds;
        var isDraft = events
            .Select(e => GetMetadataState(e.Metadata))
            .Any(state => string.Equals(state, "draft", StringComparison.OrdinalIgnoreCase));
        double requestCharge = 0;

        var isNewStream = !expectedVersion.HasValue;

        EveneumDocument header;
        string? headerETag = null;

        if (!expectedVersion.HasValue)
        {
            header = new EveneumDocument(streamId.LogicalStreamId, DocumentType.Header) { StreamId = streamId.LogicalStreamId };
            header.TimeToLive = isDraft ? timeToLive : null;
            ApplyStreamIdJson(streamId, header);
        }
        else
        {
            var headerResponse = await this.ReadHeaderDocument(streamId, cancellationToken);

            header = headerResponse.Document;
            requestCharge += headerResponse.RequestCharge;
            headerETag = header.ETag;

            if (header.Deleted)
                throw new StreamDeletedException(streamId.LogicalStreamId, requestCharge);

            if (header.Version != expectedVersion)
                throw new OptimisticConcurrencyException(streamId.LogicalStreamId, requestCharge, expectedVersion.Value, header.Version);
        }

        this.Serializer.SerializeHeaderMetadata(header, metadata);

        var ttl = isDraft ? DraftEventTimeToLive : TimeSpan.Zero;

        var baseVersion = header.Version;
        var batchSize = Math.Max(1, this.BatchSize - 1);
        var offset = 0;
        var writtenEvents = 0;
        var firstBatch = true;

        do
        {
            var batch = events.Skip(offset).Take(batchSize).ToArray();
            offset += batch.Length;
            writtenEvents += batch.Length;

            header.Version = baseVersion + (ulong)writtenEvents;

            var transaction = Persistence.CreateTransactionalBatch(streamId.ToPartitionKey());

            if (isNewStream && firstBatch)
                transaction.CreateItem(header);
            else
                transaction.ReplaceItem(header.Id, header, new TransactionalBatchItemRequestOptions { IfMatchEtag = headerETag });

            foreach (var @event in batch)
            {
                var document = this.Serializer.SerializeEvent(@event, streamId.LogicalStreamId, ttl);
                ApplyStreamIdJson(streamId, document);
                transaction.CreateItem(document);
            }

            using var response = await transaction.ExecuteAsync(cancellationToken);
            requestCharge += response.RequestCharge;

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict || response.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
            {
                if (response.GetOperationResultAtIndex<EveneumDocument>(0).StatusCode == System.Net.HttpStatusCode.Conflict || response.GetOperationResultAtIndex<EveneumDocument>(0).StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                {
                    if (isNewStream && firstBatch)
                        throw new StreamAlreadyExistsException(streamId.LogicalStreamId, requestCharge);

                    var currentHeaderResponse = await this.ReadHeaderDocument(streamId, cancellationToken);
                    requestCharge += currentHeaderResponse.RequestCharge;

                    throw new OptimisticConcurrencyException(streamId.LogicalStreamId, requestCharge, baseVersion + (ulong)(writtenEvents - batch.Length), currentHeaderResponse.Document.Version);
                }
                else
                {
                    for (var i = 0; i < batch.Length; i++)
                    {
                        if (response.GetOperationResultAtIndex<EveneumDocument>(i + 1).StatusCode == System.Net.HttpStatusCode.Conflict || response.GetOperationResultAtIndex<EveneumDocument>(i + 1).StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
                            throw new EventAlreadyExistsException(streamId.LogicalStreamId, batch[i].Version, requestCharge);
                    }
                }
            }
            else if (!response.IsSuccessStatusCode)
                throw new WriteException(streamId.LogicalStreamId, requestCharge, response.ErrorMessage, response.StatusCode);

            headerETag = response.GetOperationResultAtIndex<EveneumDocument>(0).ETag;
            firstBatch = false;
        }
        while (offset < events.Length);

        return new Response(requestCharge) { Version = header.Version };
    }

    public async Task<DeleteResponse> DeleteStream(StreamId streamId, ulong expectedVersion, CancellationToken cancellationToken = default)
    {
        var headerResponse = await this.ReadHeaderDocument(streamId, cancellationToken);

        var existingHeader = headerResponse.Document;
        var requestCharge = headerResponse.RequestCharge;

        if (existingHeader is null)
            throw new StreamNotFoundException(streamId.LogicalStreamId, requestCharge);

        if (existingHeader.Deleted)
            throw new StreamDeletedException(streamId.LogicalStreamId, requestCharge);

        if (existingHeader.Version != expectedVersion)
            throw new OptimisticConcurrencyException(streamId.LogicalStreamId, requestCharge, expectedVersion, existingHeader.Version);

        var query = $"SELECT * FROM c";

        var useSoftDeleteMode = (this.DeleteMode == DeleteMode.SoftDelete) || (this.DeleteMode == DeleteMode.TtlDelete);

        if (useSoftDeleteMode)
            query += $" WHERE c.{GetJsonPropertyName(nameof(EveneumDocument.Deleted))} = false";

        int? ttl = this.DeleteMode == DeleteMode.TtlDelete ? (int)StreamTimeToLiveAfterDelete.TotalSeconds : null;

        var deleteResponse = await this.Persistence.DeleteItems(streamId, query, useSoftDeleteMode, ttl, this.BatchSize, this.QueryMaxItemCount, cancellationToken);

        return new DeleteResponse(deleteResponse.DeletedDocuments, requestCharge + deleteResponse.RequestCharge);
    }

    public async Task<Response> CreateSnapshot(StreamId streamId, ulong version, object snapshot, object? metadata = null, bool deleteOlderSnapshots = false, CancellationToken cancellationToken = default)
    {
        var headerResponse = await this.ReadHeaderDocument(streamId, cancellationToken);

        var header = headerResponse.Document;
        var requestCharge = headerResponse.RequestCharge;

        if (header is null)
            throw new StreamNotFoundException(streamId.LogicalStreamId, requestCharge);

        if (header.Deleted)
            throw new StreamDeletedException(streamId.LogicalStreamId, requestCharge);

        if (header.Version < version)
            throw new OptimisticConcurrencyException(streamId.LogicalStreamId, requestCharge, version, header.Version);

        EveneumDocument document;

        if (this.SnapshotWriter is { } snapshotWriter && await snapshotWriter.CreateSnapshot(streamId, version, snapshot, metadata, cancellationToken))
        {
            var snapshotWriterType = snapshotWriter.GetType();
            var snapshotWriterTypeName = snapshotWriterType.AssemblyQualifiedName ?? throw new InvalidOperationException($"Snapshot writer type '{snapshotWriterType}' has no assembly-qualified name.");

            document = this.Serializer.SerializeSnapshot(
                JsonSerializer.SerializeToElement(new SnapshotWriterSnapshot(snapshotWriterTypeName), this.Serializer.JsonSerializerOptions),
                null,
                version,
                streamId.LogicalStreamId,
                this.SnapshotMode);
        }
        else
        {
            document = this.Serializer.SerializeSnapshot(
                JsonSerializer.SerializeToElement(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), this.Serializer.JsonSerializerOptions),
                metadata is not null ? JsonSerializer.SerializeToElement(metadata, this.Serializer.JsonSerializerOptions) : null,
                version,
                streamId.LogicalStreamId,
                this.SnapshotMode);
        }

        ApplyStreamIdJson(streamId, document);

        var response = await Persistence.UpsertItemAsync(document, streamId.ToPartitionKey(), cancellationToken);

        requestCharge += response.RequestCharge;

        if (deleteOlderSnapshots)
        {
            var deleteResponse = await this.DeleteSnapshots(streamId, version, cancellationToken);

            requestCharge += deleteResponse.RequestCharge;
        }

        return new Response(requestCharge);
    }

    public async Task<DeleteResponse> DeleteSnapshots(StreamId streamId, ulong olderThanVersion, CancellationToken cancellationToken = default)
    {
        var query = $"SELECT * FROM c WHERE c.{GetJsonPropertyName(nameof(EveneumDocument.DocumentType))} = 'Snapshot' AND c.{GetJsonPropertyName(nameof(EveneumDocument.Version))} < {olderThanVersion}";

        var deleteResponse = await DeleteDocuments(streamId, query, cancellationToken);

        if (this.SnapshotWriter is not null)
            await this.SnapshotWriter.DeleteSnapshots(streamId, olderThanVersion, cancellationToken);

        return deleteResponse;
    }

    public Task<Response> LoadAllEvents(Func<IReadOnlyCollection<EventData>, Task> callback, CancellationToken cancellationToken = default) =>
        this.LoadEvents($"SELECT * FROM c WHERE c.{GetJsonPropertyName(nameof(EveneumDocument.DocumentType))} = '{nameof(DocumentType.Event)}'", callback, cancellationToken);

    public Task<Response> LoadEvents(string query, Func<IReadOnlyCollection<EventData>, Task> callback, CancellationToken cancellationToken = default)
        => this.LoadEvents(new QueryDefinition(query), callback, cancellationToken);

    public Task<Response> LoadEvents(QueryDefinition query, Func<IReadOnlyCollection<EventData>, Task> callback, CancellationToken cancellationToken = default)
        => LoadDocuments(query, response => callback(response.Where(x => x.DocumentType == DocumentType.Event).Select(this.Serializer.DeserializeEvent).ToList()), cancellationToken);

    public Task<Response> LoadStreamHeaders(string query, Func<IReadOnlyCollection<StreamHeader>, Task> callback, CancellationToken cancellationToken = default)
        => this.LoadStreamHeaders(new QueryDefinition(query), callback, cancellationToken);

    public Task<Response> LoadStreamHeaders(QueryDefinition query, Func<IReadOnlyCollection<StreamHeader>, Task> callback, CancellationToken cancellationToken = default)
        => LoadDocuments(query, response => callback(response.Where(x => x.DocumentType == DocumentType.Header).Select(x => new StreamHeader(x.StreamId, x.Version, x.Metadata, x.Deleted)).ToList()), cancellationToken);

    public async Task<Response> ReplaceEvent(StreamId streamId, EventData newEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = this.Serializer.SerializeEvent(newEvent, newEvent.StreamId, DraftEventTimeToLive);
            ApplyStreamIdJson(streamId, document);

            var response = await Persistence.ReplaceItemAsync(
                document,
                EveneumDocumentSerializer.GenerateEventId(newEvent.StreamId, newEvent.Version),
                streamId.ToPartitionKey(),
                cancellationToken);

            return new Response(response.RequestCharge);
        }
        catch (CosmosException ex)
        {
            throw new WriteException(newEvent.StreamId, ex.RequestCharge, ex.Message, ex.StatusCode, ex);
        }
    }

    public async Task<DeleteResponse> DeleteEvent(StreamId streamId, ulong version, CancellationToken cancellationToken = default)
    {
        var query = $"SELECT * FROM c WHERE c.{GetJsonPropertyName(nameof(EveneumDocument.DocumentType))} = 'Event' AND c.{GetJsonPropertyName(nameof(EveneumDocument.Version))} = {version}";

        return await DeleteDocuments(streamId, query, cancellationToken);
    }

    public async Task<StreamHeaderResponse> ReadHeader(StreamId streamId, CancellationToken cancellationToken = default)
    {
        var result = await this.ReadHeaderDocument(streamId, cancellationToken);

        return new StreamHeaderResponse(new StreamHeader(streamId.LogicalStreamId, result.Document.Version, result.Document.Metadata, result.Document.Deleted), result.RequestCharge);
    }

    private async Task<Response> LoadDocuments(QueryDefinition query, Func<IEnumerable<EveneumDocument>, Task> callback, CancellationToken cancellationToken = default)
    {
        using var iterator = this.Persistence.GetItemQueryIterator(query, this.QueryMaxItemCount);

        double requestCharge = 0;
        var callbackProcessing = Task.CompletedTask;

        do
        {
            var response = await iterator.ReadNextAsync(cancellationToken);

            requestCharge += response.RequestCharge;

            await callbackProcessing;

            callbackProcessing = callback(response);
        }
        while (iterator.HasMoreResults);

        await callbackProcessing;

        return new Response(requestCharge);
    }

    private async Task<DocumentResponse> ReadHeaderDocument(StreamId streamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await this.Persistence.ReadItemAsync(streamId.LogicalStreamId, streamId.ToPartitionKey(), cancellationToken);

            return new DocumentResponse(result.Resource, result.RequestCharge);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new StreamNotFoundException(streamId.LogicalStreamId, ex.RequestCharge, ex);
        }
    }

    private async Task<DeleteResponse> DeleteDocuments(StreamId streamId, string query, CancellationToken cancellationToken)
    {
        var headerResponse = await this.ReadHeader(streamId, cancellationToken);

        var requestCharge = headerResponse.RequestCharge;
        var useSoftDeleteMode = (this.DeleteMode == DeleteMode.SoftDelete) || (this.DeleteMode == DeleteMode.TtlDelete);

        if (useSoftDeleteMode)
            query += $" AND c.{GetJsonPropertyName(nameof(EveneumDocument.Deleted))} = false";

        int? ttl = this.DeleteMode == DeleteMode.TtlDelete ? (int)StreamTimeToLiveAfterDelete.TotalSeconds : null;

        var deleteResponse = await this.Persistence.DeleteItems(streamId, query, useSoftDeleteMode, ttl, this.BatchSize, this.QueryMaxItemCount, cancellationToken);

        return new DeleteResponse(deleteResponse.DeletedDocuments, requestCharge + deleteResponse.RequestCharge);
    }
}

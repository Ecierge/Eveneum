using Eveneum.Documents;
using Eveneum.Serialization;
using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Eveneum.Tests.Infrastructure;

static class CosmosSetup
{
    public static CosmosClient GetClientWithSystemTextJson(JsonSerializerOptions? serializerOptions = null) =>
        GetClient(new SystemTextJsonCosmosSerializer(serializerOptions ?? new JsonSerializerOptions()));

    public static async Task<CosmosClient> GetClientWithSystemTextJson(string database, string container, string streamIdPropertyName, JsonSerializerOptions? serializerOptions = null)
    {
        var client = GetClientWithSystemTextJson(serializerOptions);

        await CreateContainer(database, container, streamIdPropertyName, client);

        return client;
    }

    public static Task<List<EveneumDocument>> QueryAllDocuments(CosmosClient client, string database, string collection)
        => Query(client, database, collection, "SELECT * FROM x");

    public static Task<List<EveneumDocument>> QueryAllDocumentsInStream(CosmosClient client, string database, string collection, string streamId, DocumentType? documentType = null)
        => Query(client, database, collection, $"SELECT * FROM x", new PartitionKey(streamId), documentType);

    public static string? GetEmulatorSetting(string name) =>
        Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    private static CosmosClient GetClient(CosmosSerializer serializer)
    {
        var endpoint = GetEmulatorSetting("CosmosDbEmulator.Endpoint") ?? "https://localhost:8081";
        var key = GetEmulatorSetting("CosmosDbEmulator.Key") ?? "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        var connectionMode = GetEmulatorSetting("CosmosDbEmulator.ConnectionMode") ?? "Direct";

        return new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            Serializer = serializer,
            RequestTimeout = TimeSpan.FromMinutes(1),
            MaxRetryAttemptsOnRateLimitedRequests = 3,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromMinutes(1),
            ConnectionMode = Enum.Parse<ConnectionMode>(connectionMode, true)
        });
    }

    private static async Task<List<EveneumDocument>> Query(CosmosClient client, string database, string collection, string query, PartitionKey? partitionKey = null, DocumentType? documentType = null)
    {
        using var documentQuery = client.GetDatabase(database).GetContainer(collection).GetItemQueryIterator<EveneumDocument>(query, requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var documents = new List<EveneumDocument>();

        do
        {
            var page = await documentQuery.ReadNextAsync();
            documents.AddRange(page.Where(x => !documentType.HasValue || x.DocumentType == documentType.Value));
        }
        while (documentQuery.HasMoreResults);

        return documents;
    }

    private static async Task CreateContainer(string database, string container, string streamIdPropertyName, CosmosClient client)
    {
        await client.CreateDatabaseIfNotExistsAsync(database);

        var containerProperties = new ContainerProperties(container, "/" + streamIdPropertyName) { DefaultTimeToLive = -1 };

        await client.GetDatabase(database).CreateContainerIfNotExistsAsync(containerProperties);
    }
}

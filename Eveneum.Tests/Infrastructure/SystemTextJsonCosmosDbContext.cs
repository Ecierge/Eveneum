using Eveneum.Persistence;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Eveneum.Tests.Infrastructure;

public class SystemTextJsonCosmosDbContext : CosmosDbContext
{
    public override string Container => "SystemTextJson" + this.JsonSerializerOptions.PropertyNamingPolicy?.GetType().Name;

    public override JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    public override async Task Initialize()
    {
        this.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        this.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

        this.Client = await CosmosSetup.GetClientWithSystemTextJson(this.Database, this.Container, this.StreamIdPropertyName, this.JsonSerializerOptions);
        await DeleteAllDocuments();

        this.EventStoreOptions.JsonSerializerOptions = this.JsonSerializerOptions;

        var persistence = new CosmosPersistence(this.Client, this.Database, this.Container, this.BulkDeleteMode);
        this.EventStore = new EventStore(persistence, this.EventStoreOptions);

        await this.EventStore.Initialize();
    }
}

public class SystemTextJsonLinuxCosmosDbContext : SystemTextJsonCosmosDbContext
{
    public override string Container => base.Container + "Linux";
    public override BulkDeleteMode BulkDeleteMode => Eveneum.BulkDeleteMode.TransactionalBatch;
}

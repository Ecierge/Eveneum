using System.Threading;
using System.Threading.Tasks;
using Ecierge.Eveneum;

namespace Eveneum
{
    public interface IReadStream
    {
        Task<StreamHeaderResponse> ReadHeader(StreamPartitionKey streamId, CancellationToken cancellationToken = default);
        Task<StreamResponse> ReadStream(StreamPartitionKey streamId, ReadStreamOptions options = default, CancellationToken cancellationToken = default);
    }

    public interface IWriteToStream
    {
        Task<Response> WriteToStream(StreamPartitionKey streamId, EventData[] events, ulong? expectedVersion = null, object metadata = null, CancellationToken cancellationToken = default);
    }

    public interface IDeleteStream
    {
        DeleteMode DeleteMode { get; }
        Task<DeleteResponse> DeleteStream(StreamPartitionKey streamId, ulong expectedVersion, CancellationToken cancellationToken = default);
    }

    public interface IManageSnapshots
    {
        Task<Response> CreateSnapshot(StreamPartitionKey streamId, ulong version, object snapshot, object metadata = null, bool deleteOlderSnapshots = false, CancellationToken cancellationToken = default);
        Task<DeleteResponse> DeleteSnapshots(StreamPartitionKey streamId, ulong olderThanVersion, CancellationToken cancellationToken = default);
    }

    public interface IEventStore : IReadStream, IWriteToStream, IDeleteStream, IManageSnapshots
    {
        Task Initialize(CancellationToken cancellationToken = default);
        System.Text.Json.JsonSerializerOptions JsonSerializerOptions { get; }
    }
}

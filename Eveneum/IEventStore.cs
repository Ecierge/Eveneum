using System.Threading;
using System.Threading.Tasks;
using Ecierge.Eveneum;

namespace Eveneum
{
    public interface IReadStream
    {
        Task<StreamHeaderResponse> ReadHeader(StreamId streamId, CancellationToken cancellationToken = default);
        Task<StreamResponse> ReadStream(StreamId streamId, ReadStreamOptions options = default, CancellationToken cancellationToken = default);
    }

    public interface IWriteToStream
    {
        Task<Response> WriteToStream(StreamId streamId, EventData[] events, ulong? expectedVersion = null, object metadata = null, CancellationToken cancellationToken = default);
    }

    public interface IDeleteStream
    {
        DeleteMode DeleteMode { get; }
        Task<DeleteResponse> DeleteStream(StreamId streamId, ulong expectedVersion, CancellationToken cancellationToken = default);
    }

    public interface IManageSnapshots
    {
        Task<Response> CreateSnapshot(StreamId streamId, ulong version, object snapshot, object metadata = null, bool deleteOlderSnapshots = false, CancellationToken cancellationToken = default);
        Task<DeleteResponse> DeleteSnapshots(StreamId streamId, ulong olderThanVersion, CancellationToken cancellationToken = default);
    }

    public interface IEventStore : IReadStream, IWriteToStream, IDeleteStream, IManageSnapshots
    {
        Task Initialize(CancellationToken cancellationToken = default);
    }
}

using System.Threading.Tasks;
using System.Threading;
using Ecierge.Eveneum;

namespace Eveneum.Snapshots
{
    public interface ISnapshotWriter
    {
        Task<bool> CreateSnapshot(StreamPartitionKey streamId, ulong version, object snapshot, object? metadata = null, CancellationToken cancellationToken = default);
        Task<Snapshot> ReadSnapshot(StreamPartitionKey streamId, ulong version, CancellationToken cancellationToken = default);
        Task DeleteSnapshots(StreamPartitionKey streamId, ulong olderThanVersion, CancellationToken cancellationToken = default);
    }
}

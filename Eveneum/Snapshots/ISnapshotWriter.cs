using System.Threading.Tasks;
using System.Threading;
using Ecierge.Eveneum;

namespace Eveneum.Snapshots
{
    public interface ISnapshotWriter
    {
        Task<bool> CreateSnapshot(StreamId streamId, ulong version, object snapshot, object? metadata = null, CancellationToken cancellationToken = default);
        Task<Snapshot> ReadSnapshot(StreamId streamId, ulong version, CancellationToken cancellationToken = default);
        Task DeleteSnapshots(StreamId streamId, ulong olderThanVersion, CancellationToken cancellationToken = default);
    }
}

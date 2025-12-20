using System;
using System.Collections.Generic;
using Ecierge.Eveneum;
using Eveneum.Serialization;
using Eveneum.Snapshots;
using System.Text.Json;

namespace Eveneum
{
    public class EventStoreOptions
    {
        public DeleteMode DeleteMode { get; set; } = DeleteMode.SoftDelete;
        public byte BatchSize { get; set; } = 100;
        public int QueryMaxItemCount { get; set; } = 1000;
        public JsonSerializerOptions JsonSerializerOptions { get; set; } = new JsonSerializerOptions();

        // calculate document TTL based on given timespan in case Delete mode is set to TtlDelete
        public TimeSpan StreamTimeToLiveAfterDelete { get; set; } = TimeSpan.FromHours(24);

        public ISnapshotWriter SnapshotWriter { get; set; }
        public SnapshotMode SnapshotMode { get; set; } = SnapshotMode.Multiple;

        public Action<StreamId, IDictionary<string, object?>>? StreamIdJsonMapping { get; set; }

        public TimeSpan DraftEventTimeToLive { get; set; } = TimeSpan.FromMinutes(5);
    }
}

using System;
using System.Collections.Generic;
using Ecierge.Eveneum;
using Eveneum.Serialization;
using Eveneum.Snapshots;
using Newtonsoft.Json;

namespace Eveneum
{
    public class EventStoreOptions
    {
        public DeleteMode DeleteMode { get; set; } = DeleteMode.SoftDelete;
        public byte BatchSize { get; set; } = 100;
        public int QueryMaxItemCount { get; set; } = 1000;
        public JsonSerializer JsonSerializer { get; set; } = JsonSerializer.CreateDefault();
        public ITypeProvider TypeProvider { get; set; }
        public bool IgnoreMissingTypes { get; set; } = false;

        // calculate document TTL based on given timespan in case Delete mode is set to TtlDelete
        public TimeSpan StreamTimeToLiveAfterDelete { get; set; } = TimeSpan.FromHours(24);

        public ISnapshotWriter SnapshotWriter { get; set; }
        public SnapshotMode SnapshotMode { get; set; } = SnapshotMode.Multiple;

        public Action<StreamId, IDictionary<string, string>>? StreamIdJsonMapping { get; set; }
    }
}

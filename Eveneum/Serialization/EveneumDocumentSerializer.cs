using System;
using System.Text.Json;
using Eveneum.Documents;

namespace Eveneum.Serialization
{
    public class EveneumDocumentSerializer
    {
        public JsonSerializerOptions JsonSerializerOptions { get; }

        public const char Separator = '~';

        public EveneumDocumentSerializer(JsonSerializerOptions jsonSerializer = null)
        {
            this.JsonSerializerOptions = jsonSerializer ?? new JsonSerializerOptions();
        }

        public EventData DeserializeEvent(EveneumDocument document)
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(document.Timestamp);

            return new EventData(document.StreamId, document.Body, document.Metadata, document.Version, timestamp, document.Deleted);
        }

        public Snapshot DeserializeSnapshot(EveneumDocument document) => new Snapshot(document.Body, document.Metadata, document.Version);

        internal void SerializeHeaderMetadata(EveneumDocument header, object? metadata)
        {
            if (metadata != null)
            {
                header.Metadata = JsonSerializer.SerializeToElement(metadata, this.JsonSerializerOptions);
            }
        }

        internal EveneumDocument SerializeEvent(EventData @event, string streamId)
        {
            var document = new EveneumDocument(GenerateEventId(streamId, @event.Version), DocumentType.Event)
            {
                StreamId = streamId,
                Version = @event.Version,
                Body = @event.Body,
                Metadata = @event.Metadata,
            };

            return document;
        }

        internal EveneumDocument SerializeSnapshot(JsonElement snapshot, JsonElement? metadata, ulong version, string streamId, SnapshotMode snapshotMode)
        {
            var document = new EveneumDocument(GenerateSnapshotId(snapshotMode, streamId, version), DocumentType.Snapshot)
            {
                StreamId = streamId,
                Version = version,
                Body = snapshot
            };

            if (metadata.HasValue)
            {
                document.Metadata = metadata.Value;
            }

            return document;
        }

        internal static string GenerateEventId(string streamId, ulong version) => $"{streamId}{Separator}{version}";

        internal static string GenerateSnapshotId(SnapshotMode snapshotMode, string streamId, ulong version) =>
            snapshotMode == SnapshotMode.Single
                ? $"{streamId}{Separator}S"
                : $"{streamId}{Separator}{version}{Separator}S";
    }
}

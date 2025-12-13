using System;
using System.Text.Json;
using Eveneum.Documents;

namespace Eveneum.Serialization
{
    public class EveneumDocumentSerializer
    {
        public JsonSerializerOptions JsonSerializerOptions { get; }
        public ITypeProvider TypeProvider { get; }
        public bool IgnoreMissingTypes { get; }

        public const char Separator = '~';

        public EveneumDocumentSerializer(JsonSerializerOptions jsonSerializer = null, ITypeProvider typeProvider = null, bool ignoreMissingTypes = false)
        {
            this.JsonSerializerOptions = jsonSerializer ?? new JsonSerializerOptions();
            this.TypeProvider = typeProvider ?? new PlatformTypeProvider(ignoreMissingTypes);
            this.IgnoreMissingTypes = ignoreMissingTypes;
        }

        public EventData DeserializeEvent(EveneumDocument document)
        {
            var metadata = DeserializeObject(document.MetadataType, document.Metadata);
            var body = DeserializeObject(document.BodyType, document.Body);
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(document.Timestamp);

            return new EventData(document.StreamId, body, metadata, document.Version, timestamp, document.Deleted);
        }

        public Snapshot DeserializeSnapshot(EveneumDocument document)
        {
            var metadata = DeserializeObject(document.MetadataType, document.Metadata);
            var body = DeserializeObject(document.BodyType, document.Body);

            return new Snapshot(body, metadata, document.Version);
        }

        internal void SerializeHeaderMetadata(EveneumDocument header, object? metadata)
        {
            if (metadata != null)
            {
                header.MetadataType = this.TypeProvider.GetIdentifierForType(metadata.GetType());
                header.Metadata = JsonSerializer.SerializeToElement(metadata, this.JsonSerializerOptions);
            }
        }

        internal EveneumDocument SerializeEvent(EventData @event, string streamId)
        {
            var document = new EveneumDocument(GenerateEventId(streamId, @event.Version), DocumentType.Event)
            {
                StreamId = streamId,
                Version = @event.Version,
                BodyType = this.TypeProvider.GetIdentifierForType(@event.Body.GetType()),
                Body = JsonSerializer.SerializeToElement(@event.Body, this.JsonSerializerOptions)
            };

            if (@event.Metadata != null)
            {
                document.MetadataType = this.TypeProvider.GetIdentifierForType(@event.Metadata.GetType());
                document.Metadata = JsonSerializer.SerializeToElement(@event.Metadata, this.JsonSerializerOptions);
            }

            return document;
        }

        internal EveneumDocument SerializeSnapshot(object snapshot, object? metadata, ulong version, string streamId, SnapshotMode snapshotMode)
        {
            var document = new EveneumDocument(GenerateSnapshotId(snapshotMode, streamId, version), DocumentType.Snapshot)
            {
                StreamId = streamId,
                Version = version,
                BodyType = this.TypeProvider.GetIdentifierForType(snapshot.GetType()),
                Body = JsonSerializer.SerializeToElement(snapshot, this.JsonSerializerOptions)
            };

            if (metadata != null)
            {
                document.MetadataType = this.TypeProvider.GetIdentifierForType(metadata.GetType());
                document.Metadata = JsonSerializer.SerializeToElement(metadata, this.JsonSerializerOptions);
            }

            return document;
        }

        internal object? DeserializeObject(string typeName, JsonElement data)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            var type = this.TypeProvider.GetTypeForIdentifier(typeName);

            if (type is null)
            {
                if (this.IgnoreMissingTypes)
                    return null;
                else
                    throw new TypeNotFoundException(typeName);
            }

            if (data.ValueKind == JsonValueKind.Null || data.ValueKind == JsonValueKind.Undefined)
                return null;

            try
            {
                return JsonSerializer.Deserialize(data, type, this.JsonSerializerOptions)
               ?? throw new JsonDeserializationException(typeName, data.GetRawText(), null);
            }
            catch (Exception exc)
            {
                throw new JsonDeserializationException(typeName, data.GetRawText(), exc);
            }
        }

        internal static string GenerateEventId(string streamId, ulong version) => $"{streamId}{Separator}{version}";

        internal static string GenerateSnapshotId(SnapshotMode snapshotMode, string streamId, ulong version) =>
            snapshotMode == SnapshotMode.Single
                ? $"{streamId}{Separator}S"
                : $"{streamId}{Separator}{version}{Separator}S";
    }
}

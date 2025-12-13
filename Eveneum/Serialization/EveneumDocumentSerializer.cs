using System;
using System.Text.Json;
using Eveneum.Documents;

namespace Eveneum.Serialization;

public class EveneumDocumentSerializer(JsonSerializerOptions? jsonSerializerOptions = null, ITypeProvider? typeProvider = null, bool ignoreMissingTypes = false)
{
    public JsonSerializerOptions JsonSerializerOptions { get; } = jsonSerializerOptions ?? new JsonSerializerOptions();
    public ITypeProvider TypeProvider { get; } = typeProvider ?? new PlatformTypeProvider();

    public const char Separator = '~';

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
        if (metadata is not null)
        {
            header.MetadataType = this.TypeProvider.GetIdentifierForType(metadata.GetType());
            header.Metadata = JsonSerializer.SerializeToElement(metadata, this.JsonSerializerOptions);
        }
    }

    internal EveneumDocument SerializeEvent(EventData @event, string streamId)
    {
        var body = @event.Body ?? throw new ArgumentException($"Event version {@event.Version} of stream '{streamId}' has no Body.", nameof(@event));
        var document = new EveneumDocument(GenerateEventId(streamId, @event.Version), DocumentType.Event)
        {
            StreamId = streamId,
            Version = @event.Version,
            BodyType = this.TypeProvider.GetIdentifierForType(body.GetType()),
            Body = JsonSerializer.SerializeToElement(body, this.JsonSerializerOptions)
        };

        if (@event.Metadata is not null)
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

        if (metadata is not null)
        {
            document.MetadataType = this.TypeProvider.GetIdentifierForType(metadata.GetType());
            document.Metadata = JsonSerializer.SerializeToElement(metadata, this.JsonSerializerOptions);
        }

        return document;
    }

    internal object? DeserializeObject(string? typeName, JsonElement data)
    {
        if (typeName is null || typeName.Length == 0)
            return null;

        var type = this.TypeProvider.GetTypeForIdentifier(typeName);

        if (type is null)
        {
            if (ignoreMissingTypes)
                return null;
            else
                throw new TypeNotFoundException(typeName);
        }

        if (data.ValueKind == JsonValueKind.Null || data.ValueKind == JsonValueKind.Undefined)
            return null;

        try
        {
            return JsonSerializer.Deserialize(data, type, this.JsonSerializerOptions)
                ?? throw new JsonDeserializationException(typeName, data.GetRawText(), null!);
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

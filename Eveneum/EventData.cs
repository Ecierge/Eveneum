using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Eveneum
{
    public struct EventData
    {
        public string StreamId;
        public JsonElement Body;
        public JsonElement Metadata;
        public ulong Version;
        public long Timestamp;
        public bool Deleted;

        public EventData(string streamId, JsonElement body, JsonElement metadata, ulong version, DateTimeOffset timestamp, bool deleted = false)
        {
            this.StreamId = streamId;
            this.Body = body;
            this.Metadata = metadata;
            this.Version = version;
            this.Timestamp = timestamp.ToUnixTimeSeconds();
            this.Deleted = deleted;
        }
        public EventData(string streamId, object body, object metadata, ulong version, DateTimeOffset timestamp, JsonSerializerOptions serializerOptions, bool deleted = false)
        {
            this.StreamId = streamId;
            this.Body = JsonSerializer.SerializeToElement(body, serializerOptions);
            this.Metadata = JsonSerializer.SerializeToElement(metadata, serializerOptions);
            this.Version = version;
            this.Timestamp = timestamp.ToUnixTimeSeconds();
            this.Deleted = deleted;
        }
        public JsonNode GetBodyAsNode () => JsonNode.Parse(this.Body.GetRawText())!;
        public JsonNode GetMetadataAsNode () => JsonNode.Parse(this.Metadata.GetRawText())!;

    }
}

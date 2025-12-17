using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eveneum.Documents
{
    public enum DocumentType { Header = 1, Event, Snapshot }

    public class EveneumDocument
    {
        public EveneumDocument(string id, DocumentType documentType)
        {
            this.Id = id;
            this.DocumentType = documentType;
        }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DocumentType DocumentType { get; }

        public string StreamId { get; set; }

        public ulong Version { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Metadata { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public JsonElement Body { get; set; }

        public decimal SortOrder => this.Version + GetOrderingFraction(this.DocumentType);

        public bool Deleted { get; set; }

        [JsonPropertyName("_etag")]
        public string ETag { get; set; }

        [JsonPropertyName("_ts")]
        public long Timestamp { get; set; }

        [JsonPropertyName("ttl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? TimeToLive { get; set; }

        [JsonExtensionData]
        public Dictionary<string, object?> CustomJsonProperties { get; set; } = new();

        internal static decimal GetOrderingFraction(DocumentType documentType)
        {
            switch(documentType)
            {
                case DocumentType.Header:
                    return 0.3M;
                case DocumentType.Snapshot:
                    return 0.2M;
                case DocumentType.Event:
                    return 0.1M;
                default:
                    throw new NotSupportedException($"Document type '{documentType}' is not supported.");
            }
        }
    }
}

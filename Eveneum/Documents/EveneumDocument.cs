using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Eveneum.Documents;

public enum DocumentType { Header = 1, Event, Snapshot }

public class EveneumDocument(string id, DocumentType documentType)
{
    [JsonPropertyName("id")]
    public string Id { get; set;  } = id;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DocumentType DocumentType { get; set; } = documentType;

    public string StreamId { get; set; } = null!;

    public ulong Version { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Metadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement Body { get; set; }

    public decimal SortOrder => this.Version + GetOrderingFraction(this.DocumentType);

    public bool Deleted { get; set; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    [JsonPropertyName("_ts")]
    public long Timestamp { get; set; }

    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeToLive { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?> CustomJsonProperties { get; set; } = new();

    public static decimal GetOrderingFraction(DocumentType documentType) => documentType switch
    {
        DocumentType.Header => 0.3M,
        DocumentType.Snapshot => 0.2M,
        DocumentType.Event => 0.1M,
        _ => throw new NotSupportedException($"Document type '{documentType}' is not supported."),
    };
}

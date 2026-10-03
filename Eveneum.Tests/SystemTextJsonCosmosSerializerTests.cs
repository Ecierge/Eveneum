using Eveneum.Documents;
using Eveneum.Serialization;
using NUnit.Framework;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Eveneum.Tests;

[TestFixture]
public class SystemTextJsonCosmosSerializerTests
{
    private static readonly string EventDocumentJson = """
        {
            "id": "S~3",
            "DocumentType": "Event",
            "StreamId": "S",
            "Version": 3,
            "Metadata": null,
            "Body": { "Version": 3, "Number": 1.25 },
            "SortOrder": 3.1,
            "Deleted": false,
            "_etag": "etag",
            "_ts": 1700000000,
            "ttl": null,
            "CustomProperty": "custom-value"
        }
        """;

    private static readonly string SnakeCaseEventDocumentJson = """
        {
            "id": "S~3",
            "document_type": "Event",
            "stream_id": "S",
            "version": 3,
            "body": { "version": 3, "number": 1.25 },
            "sort_order": 3.1,
            "deleted": false,
            "_etag": "etag",
            "_ts": 1700000000,
            "tenant_id": "T"
        }
        """;

    private static System.IO.Stream CreateStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Test]
    public void FromStream_ReturnsDocument()
    {
        var serializer = new SystemTextJsonCosmosSerializer();

        var document = serializer.FromStream<EveneumDocument>(CreateStream(EventDocumentJson));

        Assert.That(document, Is.Not.Null);
        AssertDocument(document);
        Assert.That(document.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(document.CustomJsonProperties["CustomProperty"]?.ToString(), Is.EqualTo("custom-value"));
    }

    [Test]
    public void FromStream_WithNamingPolicy_ReturnsDocument()
    {
        var serializer = new SystemTextJsonCosmosSerializer(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var document = serializer.FromStream<EveneumDocument>(CreateStream(SnakeCaseEventDocumentJson));

        Assert.That(document, Is.Not.Null);
        AssertDocument(document);
        Assert.That(document.Metadata.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
        Assert.That(document.CustomJsonProperties["tenant_id"]?.ToString(), Is.EqualTo("T"));
    }

    [Test]
    public void ToStream_RoundTrips()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var serializer = new SystemTextJsonCosmosSerializer(options);

        var original = new EveneumDocument("S~3", DocumentType.Event)
        {
            StreamId = "S",
            Version = 3,
            Body = JsonSerializer.SerializeToElement(new SampleEvent { Version = 3, Number = 1.25m }, options),
            ETag = "etag",
            Timestamp = 1700000000,
        };
        original.CustomJsonProperties["tenant_id"] = "T";

        using var stream = serializer.ToStream(original);
        var json = new StreamReader(stream).ReadToEnd();
        stream.Position = 0;
        var document = serializer.FromStream<EveneumDocument>(stream);

        Assert.That(json, Does.Contain("\"stream_id\":\"S\""));
        Assert.That(json, Does.Contain("\"tenant_id\":\"T\""));
        Assert.That(json, Does.Not.Contain("\"metadata\""));
        Assert.That(json, Does.Not.Contain("\"ttl\""));
        Assert.That(document, Is.Not.Null);
        AssertDocument(document);
        Assert.That(document.CustomJsonProperties["tenant_id"]?.ToString(), Is.EqualTo("T"));
    }

    private static void AssertDocument(EveneumDocument document)
    {
        Assert.Multiple(() =>
        {
            Assert.That(document.Id, Is.EqualTo("S~3"));
            Assert.That(document.DocumentType, Is.EqualTo(DocumentType.Event));
            Assert.That(document.StreamId, Is.EqualTo("S"));
            Assert.That(document.Version, Is.EqualTo(3UL));
            Assert.That(document.Body.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(document.Deleted, Is.False);
            Assert.That(document.ETag, Is.EqualTo("etag"));
            Assert.That(document.Timestamp, Is.EqualTo(1700000000L));
            Assert.That(document.TimeToLive, Is.Null);
            Assert.That(document.SortOrder, Is.EqualTo(3.1M));
        });
    }
}

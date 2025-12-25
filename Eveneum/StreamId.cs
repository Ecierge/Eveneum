using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Cosmos;

namespace Ecierge.Eveneum;

public readonly record struct StreamId
{
#pragma warning disable CA1051 // Do not declare visible instance fields
    public readonly string Value;
#pragma warning restore CA1051 // Do not declare visible instance fields
    public StreamId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
}

public record struct StreamPartitionKey
{
    public StreamPartitionKey(string? component0, string? component1, StreamId component2)
    {
        Component0 = component0;
        Component1 = component1;
        Component2 = component2.Value;
    }
    public StreamPartitionKey(string? component0, StreamId component1)
    {
        Component0 = component0;
        Component1 = component1.Value;
        Component2 = null;
    }
    public StreamPartitionKey(StreamId component0)
    {
        Component0 = component0.Value ;
        Component1 = null;
        Component2 = null;
    }

    public string? Component0 { get; private set; }
    public string? Component1 { get; private set; }
    public string? Component2 { get; private set; }

    public static implicit operator StreamPartitionKey(string streamId) =>
        new StreamPartitionKey(new StreamId(streamId));

    public static implicit operator PartitionKey(StreamPartitionKey pk) =>
        pk.ToPartitionKey();

    public string LogicalStreamId => Component2 ?? Component1 ?? Component0!;

    public override string ToString() => LogicalStreamId;

    public PartitionKey ToPartitionKey()
    {
        var partitionKeyBuilder = new PartitionKeyBuilder();
        partitionKeyBuilder.Add(Component0);
        if (Component1 is not null)
            partitionKeyBuilder.Add(Component1);
        if (Component2 is not null)
            partitionKeyBuilder.Add(Component2);
        return partitionKeyBuilder.Build();
    }

}

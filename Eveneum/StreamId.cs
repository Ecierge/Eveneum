using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Cosmos;

namespace Ecierge.Eveneum;

public record struct StreamId(string Component0, string? Component1 = null, string? Component2 = null)
{
    //public static implicit operator StreamId(string streamId) =>
    //    new StreamId(streamId);

    public string LogicalStreamId => Component2 ?? Component1 ?? Component0;

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

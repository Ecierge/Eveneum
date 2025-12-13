using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;

namespace Eveneum.Serialization
{
    public class JsonNetCosmosSerializer : Microsoft.Azure.Cosmos.CosmosSerializer
    {
        private readonly JsonSerializerOptions serializerOptions;

        public JsonNetCosmosSerializer(JsonSerializerOptions serializerOptions) =>
            this.serializerOptions = serializerOptions;

        public override T FromStream<T>([NotNull] System.IO.Stream stream)
        {
            if (typeof(Stream).IsAssignableFrom(typeof(T)))
                return (T)(object)stream;

            using (stream)
            {
                if (stream.CanSeek && stream.Length == 0)
                    return default!;

                return JsonSerializer.Deserialize<T>(stream, serializerOptions)!;
            }
        }

        public override System.IO.Stream ToStream<T>(T input)
        {
            var stream = new MemoryStream();
            JsonSerializer.Serialize(stream, input, serializerOptions);
            stream.Position = 0;

            return stream;
        }
    }
}

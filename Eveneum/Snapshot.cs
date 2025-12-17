using System.Text.Json;

namespace Eveneum
{
    public struct Snapshot
    {
        internal Snapshot(JsonElement data, JsonElement metadata, ulong version)
        {
            this.Data = data;
            this.Metadata = metadata;
            this.Version = version;
        }

        public JsonElement Data;
        public JsonElement Metadata;
        public ulong Version;
    }
}

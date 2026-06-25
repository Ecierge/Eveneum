using System.Text.Json;

namespace Eveneum
{
    public static class EventMetadataState
    {
        private const string StatePropertyName = "state";

        public static bool IsDraft(JsonElement metadata)
        {
            return metadata.ValueKind == JsonValueKind.Object
                && metadata.TryGetProperty(StatePropertyName, out var stateProperty)
                && stateProperty.ValueKind == JsonValueKind.String
                && (stateProperty.ValueEquals("Draft") || stateProperty.ValueEquals("draft"));
        }
    }
}
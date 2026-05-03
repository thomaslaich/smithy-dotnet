using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal sealed class JsonMapSerializer(Utf8JsonWriter writer) : IMapSerializer
{
    public void Entry<TState>(
        string key,
        TState state,
        Action<TState, IShapeSerializer> valueWriter
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueWriter);
        writer.WritePropertyName(key);
        // Map values are written in value-context (no property-name framing).
        var valueSerializer = new JsonShapeSerializer(writer, ownsWriter: false);
        valueWriter(state, valueSerializer);
    }
}

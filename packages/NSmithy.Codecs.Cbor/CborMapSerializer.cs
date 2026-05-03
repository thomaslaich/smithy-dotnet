using System.Formats.Cbor;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

internal sealed class CborMapSerializer(CborWriter writer) : IMapSerializer
{
    public void Entry<TState>(
        string key,
        TState state,
        Action<TState, IShapeSerializer> valueWriter
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueWriter);
        writer.WriteTextString(key);
        var valueSerializer = new CborShapeSerializer(writer);
        valueWriter(state, valueSerializer);
    }
}

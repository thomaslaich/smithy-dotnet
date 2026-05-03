namespace NSmithy.Core.Serde;

/// <summary>
/// Per-entry writer handed to map serialization callbacks. The codec is responsible for any
/// delimiters between key and value or between entries.
/// </summary>
public interface IMapSerializer
{
    /// <summary>
    /// Write a single map entry. The <paramref name="valueWriter"/> callback receives a shape
    /// serializer scoped to the entry's value position.
    /// </summary>
    void Entry<TState>(string key, TState state, Action<TState, IShapeSerializer> valueWriter);
}

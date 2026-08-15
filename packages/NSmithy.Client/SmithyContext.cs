namespace NSmithy.Client;

public sealed class SmithyContext
{
    private readonly Dictionary<ContextKey, object?> values;

    public SmithyContext()
        : this(0) { }

    /// <summary>
    /// Creates a context sized for <paramref name="capacity"/> entries. The client runtime knows how
    /// many keys it will set, and an unsized dictionary reallocates its buckets and rehashes partway
    /// through populating them — on every invocation.
    /// </summary>
    public SmithyContext(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        values = new Dictionary<ContextKey, object?>(capacity);
    }

    public void Set<T>(ContextKey<T> key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        values[key] = value;
    }

    public bool TryGet<T>(ContextKey<T> key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!values.TryGetValue(key, out var rawValue))
        {
            value = default!;
            return false;
        }

        if (rawValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        if (rawValue is null && default(T) is null)
        {
            value = default!;
            return true;
        }

        value = default!;
        return false;
    }

    public T Get<T>(ContextKey<T> key)
    {
        if (TryGet(key, out T value))
        {
            return value;
        }

        throw new KeyNotFoundException($"No Smithy context value was found for '{key.Name}'.");
    }
}

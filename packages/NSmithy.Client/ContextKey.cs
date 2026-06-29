namespace NSmithy.Client;

public abstract class ContextKey(string name, Type valueType) : IEquatable<ContextKey>
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public Type ValueType { get; } =
        valueType ?? throw new ArgumentNullException(nameof(valueType));

    public bool Equals(ContextKey? other) =>
        other is not null
        && ValueType == other.ValueType
        && StringComparer.Ordinal.Equals(Name, other.Name);

    public override bool Equals(object? obj) => obj is ContextKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(ValueType, StringComparer.Ordinal.GetHashCode(Name));

    public override string ToString() => Name;
}

public sealed class ContextKey<T>(string name) : ContextKey(name, typeof(T));

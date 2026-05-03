namespace NSmithy.Core.Serde;

/// <summary>
/// A shape that knows how to construct itself from an <see cref="IShapeDeserializer"/>.
/// Uses a static abstract factory so callers can write
/// <c>codec.Deserialize&lt;MyShape&gt;(bytes)</c> without an extra builder companion.
/// </summary>
public interface IDeserializableShape<TSelf>
    where TSelf : IDeserializableShape<TSelf>
{
    /// <summary>The shape's schema. Typically a <c>static readonly</c> field on <typeparamref name="TSelf"/>.</summary>
    static abstract Schema Schema { get; }

    /// <summary>Construct an instance of <typeparamref name="TSelf"/> from <paramref name="deserializer"/>.</summary>
    static abstract TSelf Deserialize(IShapeDeserializer deserializer);
}

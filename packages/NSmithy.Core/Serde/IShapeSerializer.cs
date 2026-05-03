using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// Visitor that walks a shape's Smithy data model and writes it to the codec's underlying
/// output. Generated shapes implement <see cref="ISerializableShape.Serialize(IShapeSerializer)"/>
/// by calling the appropriate <c>Write*</c> method for each member, threading the member's
/// schema through so codecs can consult traits without reflection.
/// </summary>
/// <remarks>
/// State-passing overloads on <see cref="WriteList{TState}"/>, <see cref="WriteMap{TState}"/>
/// avoid closure allocations on the hot path. Pass the loop state explicitly rather than
/// capturing it.
/// </remarks>
public interface IShapeSerializer : IDisposable
{
    /// <summary>Write a structure or union shape using <paramref name="schema"/>.</summary>
    void WriteStruct(Schema schema, ISerializableStruct value);

    /// <summary>Begin a list. The consumer writes <paramref name="size"/> elements.</summary>
    /// <param name="size">Number of elements, or <c>-1</c> if unknown.</param>
    void WriteList<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IShapeSerializer> consumer
    );

    /// <summary>Begin a map. The consumer writes <paramref name="size"/> entries via the map serializer.</summary>
    /// <param name="size">Number of entries, or <c>-1</c> if unknown.</param>
    void WriteMap<TState>(
        Schema schema,
        TState state,
        int size,
        Action<TState, IMapSerializer> consumer
    );

    void WriteBoolean(Schema schema, bool value);

    void WriteByte(Schema schema, sbyte value);

    void WriteShort(Schema schema, short value);

    void WriteInteger(Schema schema, int value);

    void WriteLong(Schema schema, long value);

    void WriteFloat(Schema schema, float value);

    void WriteDouble(Schema schema, double value);

    void WriteBigInteger(Schema schema, BigInteger value);

    void WriteBigDecimal(Schema schema, decimal value);

    void WriteString(Schema schema, string value);

    void WriteBlob(Schema schema, ReadOnlySpan<byte> value);

    void WriteTimestamp(Schema schema, DateTimeOffset value);

    void WriteDocument(Schema schema, Document value);

    /// <summary>Write a null value. Used for optional / sparse members.</summary>
    void WriteNull(Schema schema);

    /// <summary>Flush any buffered output to the underlying sink.</summary>
    void Flush();
}

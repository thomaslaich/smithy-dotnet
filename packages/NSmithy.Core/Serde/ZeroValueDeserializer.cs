using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// Supplies the zero value of whatever a schema reads, which is how a client fills a required
/// member its peer left out (Smithy's client error correction; see
/// <see cref="StructSchema{T, TBuilder}.CompleteMembers"/>). A union has no zero value, so reading
/// one fails as the missing member <c>memberName</c>.
/// </summary>
internal readonly struct ZeroValueDeserializer(string memberName) : IShapeDeserializer
{
    public bool TryReadNull() => false;

    public bool ReadBoolean() => false;

    public sbyte ReadByte() => 0;

    public short ReadShort() => 0;

    public int ReadInteger() => 0;

    public long ReadLong() => 0;

    public float ReadFloat() => 0;

    public double ReadDouble() => 0;

    public BigInteger ReadBigInteger() => BigInteger.Zero;

    public decimal ReadBigDecimal() => 0;

    public string ReadString() => string.Empty;

    public byte[] ReadBlob() => [];

    public DateTimeOffset ReadTimestamp() => DateTimeOffset.UnixEpoch;

    public Document ReadDocument() => Document.Null;

    // An open enum reads an unrecognized value as its unknown variant.
    public string ReadStringEnum() => string.Empty;

    public int ReadIntEnum() => 0;

    public Stream ReadStream() => Stream.Null;

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        AsyncEnumerable.Empty<TEvent>();

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema) => schema.BuildZeroValue();

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    ) => schema.Build(schema.CreateTypedBuilder());

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    ) => schema.Build(schema.CreateTypedBuilder());

    public T ReadUnion<T>(UnionSchema<T> schema) =>
        throw new MissingRequiredMemberException(memberName);
}

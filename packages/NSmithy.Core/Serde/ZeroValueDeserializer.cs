using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// Supplies the zero value of whatever a schema reads, which is how a client fills a required
/// member its peer left out (Smithy's client error correction). A structure gets its required
/// members without a default filled the same way, and its other members keep the values its
/// builder starts with.
/// A union has no zero value, so reading one fails as a missing member.
/// </summary>
internal struct ZeroValueDeserializer(string memberName) : IShapeDeserializer
{
    private string memberName = memberName;

    public readonly bool TryReadNull() => false;

    public readonly bool ReadBoolean() => false;

    public readonly sbyte ReadByte() => 0;

    public readonly short ReadShort() => 0;

    public readonly int ReadInteger() => 0;

    public readonly long ReadLong() => 0;

    public readonly float ReadFloat() => 0;

    public readonly double ReadDouble() => 0;

    public readonly BigInteger ReadBigInteger() => BigInteger.Zero;

    public readonly decimal ReadBigDecimal() => 0;

    public readonly string ReadString() => string.Empty;

    public readonly byte[] ReadBlob() => [];

    public readonly DateTimeOffset ReadTimestamp() => DateTimeOffset.UnixEpoch;

    public readonly Document ReadDocument() => Document.Null;

    // An open enum reads an unrecognized value as its unknown variant.
    public readonly string ReadStringEnum() => string.Empty;

    public readonly int ReadIntEnum() => 0;

    public readonly Stream ReadStream() => Stream.Null;

    public readonly IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        AsyncEnumerable.Empty<TEvent>();

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema)
    {
        var builder = schema.CreateTypedBuilder();
        var outer = memberName;
        var members = schema.Members;
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index] is { IsRequired: true, HasDefault: false })
            {
                memberName = members[index].Name;
                schema.DeserializeMember(builder, index, ref this);
            }
        }

        memberName = outer;
        return schema.Build(builder);
    }

    public readonly TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    ) => schema.Build(schema.CreateTypedBuilder());

    public readonly TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    ) => schema.Build(schema.CreateTypedBuilder());

    public readonly T ReadUnion<T>(UnionSchema<T> schema) =>
        throw new MissingRequiredMemberException(memberName);
}

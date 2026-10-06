using System.Globalization;
using System.Numerics;

namespace NSmithy.Core.Serde;

public static class MemberIndex
{
    /// <summary>
    /// The position of a value that is not a member of anything: the value a codec serializes or
    /// deserializes at the top level.
    /// </summary>
    public const int Root = -1;
}

/// <summary>
/// Receives values from a schema's serialization code. Every call names the member being written
/// by its index in the shape being serialized (<see cref="MemberIndex.Root"/> for the top-level
/// value) and passes the value at its static type. A nested aggregate is written through its own
/// schema, which writes its members into the serializer positioned on that aggregate.
/// </summary>
public interface IShapeSerializer
{
    void WriteNull(int member);

    void WriteBoolean(int member, bool value);

    void WriteByte(int member, sbyte value);

    void WriteShort(int member, short value);

    void WriteInteger(int member, int value);

    void WriteLong(int member, long value);

    void WriteFloat(int member, float value);

    void WriteDouble(int member, double value);

    void WriteBigInteger(int member, BigInteger value);

    void WriteBigDecimal(int member, decimal value);

    void WriteString(int member, string value);

    void WriteBlob(int member, byte[] value);

    void WriteTimestamp(int member, DateTimeOffset value);

    void WriteDocument(int member, Document value);

    void WriteStringEnum(int member, string value);

    void WriteIntEnum(int member, int value);

    void WriteStream(int member, Stream value);

    void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    );

    void WriteStruct<T>(int member, T value, IStructSchema<T> schema);

    void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    );

    void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    );

    void WriteUnion<T>(int member, T value, IUnionSchema<T> schema);
}

/// <summary>
/// Supplies values to a schema's deserialization code. A simple read returns the value the
/// deserializer is positioned on; an aggregate read walks the input and calls back into the
/// aggregate's schema for each member, element, entry, or case it finds.
/// </summary>
public interface IShapeDeserializer
{
    /// <summary>Consumes and returns true when the current value is null.</summary>
    bool TryReadNull();

    bool ReadBoolean();

    sbyte ReadByte();

    short ReadShort();

    int ReadInteger();

    long ReadLong();

    float ReadFloat();

    double ReadDouble();

    BigInteger ReadBigInteger();

    decimal ReadBigDecimal();

    string ReadString();

    byte[] ReadBlob();

    DateTimeOffset ReadTimestamp();

    Document ReadDocument();

    string ReadStringEnum();

    int ReadIntEnum();

    Stream ReadStream();

    IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema);

    T ReadStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema);

    TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    );

    TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    );

    T ReadUnion<T>(IUnionSchema<T> schema);
}

/// <summary>
/// Reads a value from its Smithy <see cref="Document"/> form, as a modeled <c>@default</c> is
/// stored. Each read creates a new value, so a mutable default is never shared between objects.
/// </summary>
public struct DocumentDeserializer(Document value) : IShapeDeserializer
{
    private Document current = value;

    public readonly bool TryReadNull() => current.Kind == DocumentKind.Null;

    public readonly bool ReadBoolean() => current.AsBoolean();

    public readonly sbyte ReadByte() => (sbyte)current.AsNumber();

    public readonly short ReadShort() => (short)current.AsNumber();

    public readonly int ReadInteger() => (int)current.AsNumber();

    public readonly long ReadLong() => (long)current.AsNumber();

    public readonly float ReadFloat() =>
        current.Kind == DocumentKind.String
            ? float.Parse(current.AsString(), CultureInfo.InvariantCulture)
            : (float)current.AsNumber();

    public readonly double ReadDouble() =>
        current.Kind == DocumentKind.String
            ? double.Parse(current.AsString(), CultureInfo.InvariantCulture)
            : (double)current.AsNumber();

    public readonly BigInteger ReadBigInteger() => new(current.AsNumber());

    public readonly decimal ReadBigDecimal() => current.AsNumber();

    public readonly string ReadString() => current.AsString();

    public readonly byte[] ReadBlob() => Convert.FromBase64String(current.AsString());

    public readonly DateTimeOffset ReadTimestamp() =>
        current.Kind == DocumentKind.String
            ? DateTimeOffset.Parse(current.AsString(), CultureInfo.InvariantCulture)
            : DateTimeOffset.UnixEpoch.AddTicks(
                (long)(current.AsNumber() * TimeSpan.TicksPerSecond)
            );

    public readonly Document ReadDocument() => current;

    public readonly string ReadStringEnum() => current.AsString();

    public readonly int ReadIntEnum() => (int)current.AsNumber();

    public readonly Stream ReadStream() =>
        throw new NotSupportedException("A streaming blob has no document form.");

    public readonly IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("An event stream has no document form.");

    public T ReadStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var builder = schema.CreateTypedBuilder();
        var members = current.AsObject();
        for (var index = 0; index < schema.Members.Count; index++)
        {
            if (
                members.TryGetValue(schema.Members[index].Name, out var member)
                && member.Kind != DocumentKind.Null
            )
            {
                var nested = new DocumentDeserializer(member);
                schema.DeserializeMember(builder, index, ref nested);
            }
        }

        return schema.Build(builder);
    }

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        var builder = schema.CreateTypedBuilder();
        foreach (var element in current.AsArray())
        {
            var nested = new DocumentDeserializer(element);
            schema.DeserializeElement(builder, ref nested);
        }

        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        var builder = schema.CreateTypedBuilder();
        foreach (var (key, entry) in current.AsObject())
        {
            var nested = new DocumentDeserializer(entry);
            schema.DeserializeEntry(builder, key, ref nested);
        }

        return schema.Build(builder);
    }

    public T ReadUnion<T>(IUnionSchema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        foreach (var (name, value) in current.AsObject())
        {
            var index = schema.IndexOf(name);
            if (index >= 0)
            {
                var nested = new DocumentDeserializer(value);
                return schema.DeserializeCase(index, ref nested);
            }
        }

        throw new InvalidOperationException($"Document names no case of union '{schema.Id}'.");
    }
}

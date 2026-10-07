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

    /// <summary>
    /// Whether a member with a modeled default writes that default when its value is absent. Which
    /// messages carry defaults is the format's choice: a client's request body leaves them out, a
    /// response writes them.
    /// </summary>
    bool WritesDefault(int member);

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

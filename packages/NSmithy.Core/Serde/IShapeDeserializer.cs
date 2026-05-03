using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// Visitor that reads a shape's Smithy data model from the codec's underlying input.
/// Generated shapes implement <see cref="IDeserializableShape{TSelf}.Deserialize(IShapeDeserializer)"/>
/// by calling the appropriate <c>Read*</c> method for each member.
/// </summary>
public interface IShapeDeserializer : IDisposable
{
    /// <summary>
    /// Read a structure or union value. The consumer is invoked once per declared member
    /// present in the input, in the order they appear; unknown members may be reported via
    /// <see cref="StructMemberConsumer{TState}.UnknownMember"/>.
    /// </summary>
    void ReadStruct<TState>(Schema schema, TState state, StructMemberConsumer<TState> consumer);

    /// <summary>Read a list value, invoking <paramref name="consumer"/> once per element.</summary>
    void ReadList<TState>(Schema schema, TState state, ListMemberConsumer<TState> consumer);

    /// <summary>Read a map value, invoking <paramref name="consumer"/> once per entry.</summary>
    void ReadMap<TState>(Schema schema, TState state, MapMemberConsumer<TState> consumer);

    bool ReadBoolean(Schema schema);

    sbyte ReadByte(Schema schema);

    short ReadShort(Schema schema);

    int ReadInteger(Schema schema);

    long ReadLong(Schema schema);

    float ReadFloat(Schema schema);

    double ReadDouble(Schema schema);

    BigInteger ReadBigInteger(Schema schema);

    decimal ReadBigDecimal(Schema schema);

    string ReadString(Schema schema);

    byte[] ReadBlob(Schema schema);

    DateTimeOffset ReadTimestamp(Schema schema);

    Document ReadDocument(Schema schema);

    /// <summary>
    /// Returns <c>true</c> if the next value in the underlying input is null. Useful when
    /// reading sparse list / map members.
    /// </summary>
    bool IsNull();

    /// <summary>Consume a null value. Only meaningful after <see cref="IsNull"/> returns <c>true</c>.</summary>
    void ReadNull();

    /// <summary>
    /// If the next value is a list or map, returns its element / entry count, or <c>-1</c>
    /// when the count is unknown ahead of streaming.
    /// </summary>
    int ContainerSize() => -1;
}

/// <summary>Callback invoked once per struct/union member encountered during deserialization.</summary>
public readonly record struct StructMemberConsumer<TState>(
    Action<TState, Schema, IShapeDeserializer> Member,
    Action<TState, string, IShapeDeserializer>? UnknownMember = null
);

/// <summary>Callback invoked once per list element.</summary>
public delegate void ListMemberConsumer<in TState>(TState state, IShapeDeserializer reader);

/// <summary>Callback invoked once per map entry.</summary>
public delegate void MapMemberConsumer<in TState>(
    TState state,
    string key,
    IShapeDeserializer reader
);

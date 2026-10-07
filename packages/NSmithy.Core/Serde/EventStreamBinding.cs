using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace NSmithy.Core.Serde;

/// <summary>
/// The event-stream member of an operation's input or output structure, bound with the structure's
/// builder type and the stream's event type. A protocol reads the events off a value with
/// <see cref="GetEvents"/> and assembles a value around incoming events with <see cref="Build"/>.
/// </summary>
public sealed class EventStreamBinding<TShape, TBuilder, TEvent>
{
    private readonly int index;

    internal EventStreamBinding(
        IStructSchema<TShape, TBuilder> structure,
        int index,
        Schema<TEvent> eventSchema
    )
    {
        Structure = structure;
        this.index = index;
        EventSchema = eventSchema;
    }

    public IStructSchema<TShape, TBuilder> Structure { get; }

    public IMemberSchema Member => Structure.Members[index];

    public Schema<TEvent> EventSchema { get; }

    /// <summary>
    /// Whether the structure has members besides the event stream. Protocols that support them
    /// send them as an initial message ahead of the events.
    /// </summary>
    public bool HasInitialMembers => Structure.Members.Count > 1;

    /// <summary>The structure's members other than the event stream.</summary>
    public StructProjection<TShape, TBuilder> InitialMembers
    {
        get
        {
            var member = Member;
            return Schemas.Project(Structure, other => !ReferenceEquals(other, member));
        }
    }

    public IAsyncEnumerable<TEvent> GetEvents(TShape shape)
    {
        var capture = new EventCapture<TEvent>(index);
        Structure.SerializeMembers(shape, ref capture);
        return capture.Events
            ?? throw new InvalidOperationException(
                $"Event stream member '{Member.Name}' was null."
            );
    }

    /// <summary>
    /// Builds a value carrying <paramref name="events"/>. <paramref name="readInitialMembers"/>
    /// fills the other members first, for protocols that send them ahead of the stream.
    /// </summary>
    public TShape Build(
        IAsyncEnumerable<TEvent> events,
        Action<TBuilder>? readInitialMembers = null
    )
    {
        var builder = Structure.CreateTypedBuilder();
        readInitialMembers?.Invoke(builder);
        var deserializer = new EventSource<TEvent>(events);
        Structure.DeserializeMember(builder, index, ref deserializer);
        return Structure.Build(builder);
    }
}

/// <summary>
/// Receives an <see cref="EventStreamBinding{TShape, TBuilder, TEvent}"/> with its builder and
/// event types in scope.
/// </summary>
public interface IEventStreamBindingVisitor<TShape, out TResult>
{
    TResult Visit<TBuilder, TEvent>(EventStreamBinding<TShape, TBuilder, TEvent> binding);
}

public static class EventStreamBinding
{
    /// <summary>
    /// Binds the event-stream member of <paramref name="schema"/> and hands it to
    /// <paramref name="visitor"/>. Returns false when the schema is not a structure or has no
    /// event-stream member; throws when it has more than one.
    /// </summary>
    public static bool TryBind<TShape, TResult>(
        Schema<TShape> schema,
        IEventStreamBindingVisitor<TShape, TResult> visitor,
        [MaybeNullWhen(false)] out TResult result
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(visitor);

        if (schema.Resolved is not IStructSchema<TShape> structure)
        {
            result = default;
            return false;
        }

        var members = structure.Members;
        var index = -1;
        for (var position = 0; position < members.Count; position++)
        {
            if (members[position].Target.Resolved is not IEventStreamSchema)
            {
                continue;
            }

            if (index >= 0)
            {
                var id = members[position].Id;
                throw new InvalidOperationException(
                    $"Shape '{id.Namespace}#{id.Name}' has more than one event stream member."
                );
            }

            index = position;
        }

        if (index < 0)
        {
            result = default;
            return false;
        }

        result = structure.Accept(new StructBinder<TShape, TResult>(index, visitor))!;
        return true;
    }

    private sealed class StructBinder<TShape, TResult>(
        int index,
        IEventStreamBindingVisitor<TShape, TResult> visitor
    ) : IStructSchemaVisitor<TShape, TResult>
    {
        public TResult Visit<TBuilder>(IStructSchema<TShape, TBuilder> structure) =>
            ((IEventStreamSchema)structure.Members[index].Target.Resolved).Accept(
                new StreamBinder<TShape, TBuilder, TResult>(structure, index, visitor)
            );
    }

    private sealed class StreamBinder<TShape, TBuilder, TResult>(
        IStructSchema<TShape, TBuilder> structure,
        int index,
        IEventStreamBindingVisitor<TShape, TResult> visitor
    ) : IEventStreamSchemaVisitor<TResult>
    {
        public TResult Visit<TEvent>(EventStreamSchema<TEvent> schema) =>
            visitor.Visit(
                new EventStreamBinding<TShape, TBuilder, TEvent>(
                    structure,
                    index,
                    schema.TypedEventSchema
                )
            );
    }
}

/// <summary>Captures the event stream a structure writes as member <c>index</c>, ignoring the rest.</summary>
internal struct EventCapture<TEvent>(int index) : IShapeSerializer
{
    public IAsyncEnumerable<TEvent>? Events { get; private set; }

    public readonly bool WritesDefault(int member) => false;

    public readonly void WriteNull(int member) { }

    public readonly void WriteBoolean(int member, bool value) { }

    public readonly void WriteByte(int member, sbyte value) { }

    public readonly void WriteShort(int member, short value) { }

    public readonly void WriteInteger(int member, int value) { }

    public readonly void WriteLong(int member, long value) { }

    public readonly void WriteFloat(int member, float value) { }

    public readonly void WriteDouble(int member, double value) { }

    public readonly void WriteBigInteger(int member, BigInteger value) { }

    public readonly void WriteBigDecimal(int member, decimal value) { }

    public readonly void WriteString(int member, string value) { }

    public readonly void WriteBlob(int member, byte[] value) { }

    public readonly void WriteTimestamp(int member, DateTimeOffset value) { }

    public readonly void WriteDocument(int member, Document value) { }

    public readonly void WriteStringEnum(int member, string value) { }

    public readonly void WriteIntEnum(int member, int value) { }

    public readonly void WriteStream(int member, Stream value) { }

    public void WriteEventStream<T>(int member, IAsyncEnumerable<T> events, Schema<T> eventSchema)
    {
        if (member == index)
        {
            Events = (IAsyncEnumerable<TEvent>)events;
        }
    }

    public readonly void WriteStruct<T>(int member, T value, IStructSchema<T> schema) { }

    public readonly void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    ) { }

    public readonly void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    ) { }

    public readonly void WriteUnion<T>(int member, T value, IUnionSchema<T> schema) { }
}

/// <summary>Supplies an event stream to the member that reads one.</summary>
internal readonly struct EventSource<TEvent>(IAsyncEnumerable<TEvent> events) : IShapeDeserializer
{
    public bool TryReadNull() => false;

    public IAsyncEnumerable<T> ReadEventStream<T>(Schema<T> eventSchema) =>
        (IAsyncEnumerable<T>)events;

    public bool ReadBoolean() => throw NotAStream();

    public sbyte ReadByte() => throw NotAStream();

    public short ReadShort() => throw NotAStream();

    public int ReadInteger() => throw NotAStream();

    public long ReadLong() => throw NotAStream();

    public float ReadFloat() => throw NotAStream();

    public double ReadDouble() => throw NotAStream();

    public BigInteger ReadBigInteger() => throw NotAStream();

    public decimal ReadBigDecimal() => throw NotAStream();

    public string ReadString() => throw NotAStream();

    public byte[] ReadBlob() => throw NotAStream();

    public DateTimeOffset ReadTimestamp() => throw NotAStream();

    public Document ReadDocument() => throw NotAStream();

    public string ReadStringEnum() => throw NotAStream();

    public int ReadIntEnum() => throw NotAStream();

    public Stream ReadStream() => throw NotAStream();

    public T ReadStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) => throw NotAStream();

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => throw NotAStream();

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => throw NotAStream();

    public T ReadUnion<T>(IUnionSchema<T> schema) => throw NotAStream();

    private static InvalidOperationException NotAStream() =>
        new("The bound member does not target an event stream.");
}

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using NSmithy.Core.Validation;

namespace NSmithy.Core.Serde;

public abstract class Schema
{
    private readonly ShapeId id;
    private readonly ShapeKind kind;
    private readonly IReadOnlyDictionary<ShapeId, Trait> traits;

    protected Schema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.kind = kind;
        this.traits = Trait.Index(traits);
    }

    protected Schema()
    {
        traits = Trait.None;
    }

    public virtual ShapeId Id => id;

    public virtual ShapeKind Kind => kind;

    public virtual IReadOnlyDictionary<ShapeId, Trait> Traits => traits;

    public virtual Schema Resolved => this;

    public bool IsMember => Id.IsMember;

    public string? MemberName => Id.MemberName;

    public virtual Trait? GetTrait(ShapeId id) =>
        Traits.TryGetValue(id, out var trait) ? trait : null;

    public virtual bool HasTrait(ShapeId id) => Traits.ContainsKey(id);

}

public abstract class Schema<T> : Schema
{
    protected Schema()
        : base() { }

    protected Schema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
        : base(id, kind, traits) { }

    /// <summary>
    /// Writes <paramref name="value"/> as member <paramref name="member"/> of the shape being
    /// serialized, or as the top-level value when <paramref name="member"/> is
    /// <see cref="MemberIndex.Root"/>.
    /// </summary>
    public abstract void Write<TSerializer>(int member, T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    /// <summary>Reads the value <paramref name="deserializer"/> is positioned on.</summary>
    public abstract T Read<TDeserializer>(ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;

}

public sealed class LazySchema<T> : Schema<T>
{
    private readonly Lazy<Schema<T>> target;

    internal LazySchema(Func<Schema<T>> resolve)
        : base()
    {
        ArgumentNullException.ThrowIfNull(resolve);
        target = new Lazy<Schema<T>>(resolve);
    }

    public Schema<T> TargetSchema => target.Value;

    public override ShapeId Id => TargetSchema.Id;

    public override ShapeKind Kind => TargetSchema.Kind;

    public override IReadOnlyDictionary<ShapeId, Trait> Traits => TargetSchema.Traits;

    public override Schema Resolved => TargetSchema.Resolved;

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer) =>
        TargetSchema.Write(member, value, ref serializer);

    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        TargetSchema.Read(ref deserializer);
}

public abstract class PrimitiveSchema<T> : Schema<T>
{
    private protected PrimitiveSchema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
        : base(id, kind, traits) { }
}

public sealed class BooleanSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<bool>(id, ShapeKind.Boolean, traits)
{
    public override void Write<TSerializer>(int member, bool value, ref TSerializer serializer) =>
        serializer.WriteBoolean(member, value);

    public override bool Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadBoolean();
}

public sealed class ByteSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<sbyte>(id, ShapeKind.Byte, traits)
{
    public override void Write<TSerializer>(int member, sbyte value, ref TSerializer serializer) =>
        serializer.WriteByte(member, value);

    public override sbyte Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadByte();
}

public sealed class ShortSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<short>(id, ShapeKind.Short, traits)
{
    public override void Write<TSerializer>(int member, short value, ref TSerializer serializer) =>
        serializer.WriteShort(member, value);

    public override short Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadShort();
}

public sealed class IntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<int>(id, ShapeKind.Integer, traits)
{
    public override void Write<TSerializer>(int member, int value, ref TSerializer serializer) =>
        serializer.WriteInteger(member, value);

    public override int Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadInteger();
}

public sealed class LongSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<long>(id, ShapeKind.Long, traits)
{
    public override void Write<TSerializer>(int member, long value, ref TSerializer serializer) =>
        serializer.WriteLong(member, value);

    public override long Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadLong();
}

public sealed class FloatSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<float>(id, ShapeKind.Float, traits)
{
    public override void Write<TSerializer>(int member, float value, ref TSerializer serializer) =>
        serializer.WriteFloat(member, value);

    public override float Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadFloat();
}

public sealed class DoubleSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<double>(id, ShapeKind.Double, traits)
{
    public override void Write<TSerializer>(int member, double value, ref TSerializer serializer) =>
        serializer.WriteDouble(member, value);

    public override double Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadDouble();
}

public sealed class BigIntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<BigInteger>(id, ShapeKind.BigInteger, traits)
{
    public override void Write<TSerializer>(
        int member,
        BigInteger value,
        ref TSerializer serializer
    ) => serializer.WriteBigInteger(member, value);

    public override BigInteger Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadBigInteger();
}

public sealed class BigDecimalSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<decimal>(id, ShapeKind.BigDecimal, traits)
{
    public override void Write<TSerializer>(
        int member,
        decimal value,
        ref TSerializer serializer
    ) => serializer.WriteBigDecimal(member, value);

    public override decimal Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadBigDecimal();
}

public sealed class StringSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<string>(id, ShapeKind.String, traits)
{
    public override void Write<TSerializer>(int member, string value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteString(member, value);
        }
    }

    public override string Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadString();
}

public sealed class BlobSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<byte[]>(id, ShapeKind.Blob, traits)
{
    public override void Write<TSerializer>(int member, byte[] value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteBlob(member, value);
        }
    }

    public override byte[] Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadBlob();
}

public sealed class StreamingBlobSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<Stream>(id, ShapeKind.Blob, traits)
{
    public override void Write<TSerializer>(int member, Stream value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteStream(member, value);
        }
    }

    public override Stream Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadStream();
}

public sealed class TimestampSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<DateTimeOffset>(id, ShapeKind.Timestamp, traits)
{
    public override void Write<TSerializer>(
        int member,
        DateTimeOffset value,
        ref TSerializer serializer
    ) => serializer.WriteTimestamp(member, value);

    public override DateTimeOffset Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadTimestamp();
}

public sealed class DocumentSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<Document>(id, ShapeKind.Document, traits)
{
    public override void Write<TSerializer>(
        int member,
        Document value,
        ref TSerializer serializer
    ) => serializer.WriteDocument(member, value);

    public override Document Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadDocument();
}

public interface IMemberSchema
{
    ShapeId Id { get; }

    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> MemberTraits { get; }

    Schema Target { get; }

    bool IsRequired { get; }

    /// <summary>The effective trait: one on the member wins over the same trait on its target.</summary>
    Trait? GetTrait(ShapeId id) =>
        MemberTraits.TryGetValue(id, out var trait) ? trait : Target.GetTrait(id);

    bool HasTrait(ShapeId id) => GetTrait(id) is not null;
}

/// <summary>
/// A member of a structure, list, or map: its name, the shape it targets, and the traits declared
/// on it. A member's position in its container is its index on the wire; the container gives the
/// member its id.
/// </summary>
public sealed class MemberSchema : IMemberSchema
{
    private ShapeId? id;

    public MemberSchema(
        string name,
        Schema target,
        bool isRequired = false,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);
        Name = name;
        Target = target;
        IsRequired = isRequired;
        MemberTraits = Trait.Index(traits);
    }

    public ShapeId Id =>
        id ?? throw new InvalidOperationException($"Member '{Name}' belongs to no shape.");

    public string Name { get; }

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits { get; }

    public Schema Target { get; }

    public bool IsRequired { get; }

    internal MemberSchema BindTo(ShapeId container)
    {
        if (id is not null)
        {
            throw new InvalidOperationException($"Member '{Name}' already belongs to '{id}'.");
        }

        id = container.WithMember(Name);
        return this;
    }
}

public interface IStructSchema
{
    IMemberSchema? GetMember(string name);

    /// <summary>The members in declaration order; a member's position is its index.</summary>
    IReadOnlyList<IMemberSchema> Members { get; }

    /// <summary>Writes a value of this structure with no member set, as member <paramref name="member"/>.</summary>
    void WriteEmpty<TSerializer>(int member, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;
}

public interface IStructSchema<T> : IStructSchema
{
    /// <summary>
    /// Dispatches to <paramref name="visitor"/> with this structure's otherwise hidden builder type.
    /// </summary>
    TResult Accept<TResult>(IStructSchemaVisitor<T, TResult> visitor);

    T BuildEmpty();

    /// <summary>Writes each member of <paramref name="value"/> under its index.</summary>
    void SerializeMembers<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;
}

/// <summary>
/// Recovers the builder type hidden by <see cref="IStructSchema{T}"/> without reflection or the
/// runtime binder.
/// </summary>
public interface IStructSchemaVisitor<T, out TResult>
{
    TResult Visit<TBuilder>(IStructSchema<T, TBuilder> schema);
}

public interface IStructSchema<T, TBuilder> : IStructSchema<T>
{
    TBuilder CreateTypedBuilder();

    T Build(TBuilder builder);

    /// <summary>Reads member <paramref name="index"/> into <paramref name="builder"/>.</summary>
    void DeserializeMember<TDeserializer>(
        TBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

/// <summary>
/// A structure. Code generation derives one class per structure, which reads and writes the
/// structure's properties directly; this base carries what every structure has in common.
/// </summary>
public abstract class StructSchema<T, TBuilder> : Schema<T>, IStructSchema<T, TBuilder>
{
    private readonly MemberSchema[] members;
    private readonly Dictionary<string, IMemberSchema> membersByName;

    protected StructSchema(
        ShapeId id,
        IEnumerable<MemberSchema> members,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Structure, traits)
    {
        ArgumentNullException.ThrowIfNull(members);
        this.members = [.. members.Select(member => member.BindTo(id))];
        membersByName = this.members.ToDictionary(
            member => member.Name,
            member => (IMemberSchema)member,
            StringComparer.Ordinal
        );
    }

    public IReadOnlyList<IMemberSchema> Members => members;

    public IMemberSchema? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteStruct(member, value, this);
        }
    }

    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadStruct(this);

    public abstract void SerializeMembers<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    public abstract void DeserializeMember<TDeserializer>(
        TBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;

    public abstract TBuilder CreateTypedBuilder();

    public abstract T Build(TBuilder builder);

    public T BuildEmpty() => Build(CreateTypedBuilder());

    public void WriteEmpty<TSerializer>(int member, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct =>
        Write(member, BuildEmpty(), ref serializer);

    public TResult Accept<TResult>(IStructSchemaVisitor<T, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }
}

public sealed class UnitSchema : Schema<SmithyUnit>, IStructSchema<SmithyUnit, SmithyUnit>
{
    internal UnitSchema()
        : base(new ShapeId("smithy.api", "Unit"), ShapeKind.Structure) { }

    public IMemberSchema? GetMember(string name) => null;

    public IReadOnlyList<IMemberSchema> Members => [];

    public override void Write<TSerializer>(
        int member,
        SmithyUnit value,
        ref TSerializer serializer
    ) => serializer.WriteStruct(member, value, this);

    public override SmithyUnit Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadStruct(this);

    public void SerializeMembers<TSerializer>(SmithyUnit value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct { }

    public void DeserializeMember<TDeserializer>(
        SmithyUnit builder,
        int index,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct { }

    public SmithyUnit CreateTypedBuilder() => SmithyUnit.Value;

    public SmithyUnit Build(SmithyUnit builder) => SmithyUnit.Value;

    public SmithyUnit BuildEmpty() => SmithyUnit.Value;

    public void WriteEmpty<TSerializer>(int member, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct =>
        Write(member, SmithyUnit.Value, ref serializer);

    public TResult Accept<TResult>(IStructSchemaVisitor<SmithyUnit, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }
}

public interface INullableSchema
{
    Schema Target { get; }
}

public sealed class NullableSchema<T> : Schema<T?>, INullableSchema
    where T : struct
{
    internal NullableSchema(Schema<T> target)
        : base(target.Id, target.Kind, target.Traits.Values)
    {
        TypedTarget = target;
    }

    public Schema<T> TypedTarget { get; }

    public Schema Target => TypedTarget;

    public override void Write<TSerializer>(int member, T? value, ref TSerializer serializer)
    {
        if (value is { } present)
        {
            TypedTarget.Write(member, present, ref serializer);
        }
        else
        {
            serializer.WriteNull(member);
        }
    }

    public override T? Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.TryReadNull() ? null : TypedTarget.Read(ref deserializer);
}

public interface IStringEnumSchema
{
    /// <summary>Whether the value is one the model declares.</summary>
    bool Contains(string value);

    /// <summary>Every value the model declares.</summary>
    IReadOnlyList<string> Values { get; }

    /// <summary>
    /// The values without <c>@internal</c> — what a caller is told when a value is rejected. See
    /// <see cref="StringEnumSchema{T}.PublishedValues"/>.
    /// </summary>
    IReadOnlyList<string> PublishedValues { get; }
}

public interface IStringEnumValue
{
    string Value { get; }
}

public interface IStringEnumValue<TSelf> : IStringEnumValue
    where TSelf : IStringEnumValue<TSelf>
{
    static abstract TSelf FromValue(string value);
}

public sealed class StringEnumSchema<T> : Schema<T>, IStringEnumSchema
    where T : IStringEnumValue<T>
{
    internal StringEnumSchema(
        ShapeId id,
        IEnumerable<string>? values = null,
        IEnumerable<Trait>? traits = null,
        IEnumerable<string>? internalValues = null
    )
        : base(id, ShapeKind.Enum, traits)
    {
        Values = values is null ? [] : [.. values];
        lookup = Values.ToFrozenSet(StringComparer.Ordinal);
        var hidden = internalValues is null
            ? []
            : internalValues.ToFrozenSet(StringComparer.Ordinal);
        PublishedValues = hidden.Count == 0 ? Values : [.. Values.Where(v => !hidden.Contains(v))];
    }

    private readonly FrozenSet<string> lookup;

    public bool Contains(string value) => lookup.Contains(value);

    /// <summary>Every value the model declares, which is what membership is decided against.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// The values without <c>@internal</c>. A caller sees these listed when a value is rejected: an
    /// internal member is still accepted on the wire, but naming it back would advertise it.
    /// </summary>
    public IReadOnlyList<string> PublishedValues { get; }

    public T Create(string value) => T.FromValue(value);

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteStringEnum(member, value.Value);
        }
    }

    // A format whose enums are ordinals (protobuf) has no value for an ordinal the model does not
    // know, and reads it as null.
    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadStringEnum() is { } value ? T.FromValue(value) : default!;
}

public interface IIntEnumSchema
{
    /// <summary>Whether the value is one the model declares.</summary>
    bool Contains(int value);

    IReadOnlyList<int> Values { get; }
}

public sealed class IntEnumSchema<T> : Schema<T>, IIntEnumSchema
    where T : struct, Enum
{
    internal IntEnumSchema(
        ShapeId id,
        IEnumerable<int>? values = null,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.IntEnum, traits)
    {
        Values = values is null ? [] : [.. values];
        lookup = Values.ToFrozenSet();
    }

    private readonly FrozenSet<int> lookup;

    public bool Contains(int value) => lookup.Contains(value);

    public IReadOnlyList<int> Values { get; }

    public int GetIntegerValue(T value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

    public T Create(int value) => (T)Enum.ToObject(typeof(T), value);

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer) =>
        serializer.WriteIntEnum(member, GetIntegerValue(value));

    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        Create(deserializer.ReadIntEnum());
}

public interface IEventStreamSchema
{
    Schema EventSchema { get; }

    /// <summary>Dispatches to <paramref name="visitor"/> with the event type in scope.</summary>
    TResult Accept<TResult>(IEventStreamSchemaVisitor<TResult> visitor);
}

/// <summary>
/// Recovers the event type hidden by <see cref="IEventStreamSchema"/> without reflection or the
/// runtime binder.
/// </summary>
public interface IEventStreamSchemaVisitor<out TResult>
{
    TResult Visit<TEvent>(EventStreamSchema<TEvent> schema);
}

public sealed class EventStreamSchema<TEvent> : Schema<IAsyncEnumerable<TEvent>>, IEventStreamSchema
{
    internal EventStreamSchema(Schema<TEvent> eventSchema)
        : base(eventSchema.Id, eventSchema.Kind, eventSchema.Traits.Values)
    {
        ArgumentNullException.ThrowIfNull(eventSchema);
        TypedEventSchema = eventSchema;
    }

    public Schema<TEvent> TypedEventSchema { get; }

    public Schema EventSchema => TypedEventSchema;

    public TResult Accept<TResult>(IEventStreamSchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }

    public override void Write<TSerializer>(
        int member,
        IAsyncEnumerable<TEvent> value,
        ref TSerializer serializer
    )
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteEventStream(member, value, TypedEventSchema);
        }
    }

    public override IAsyncEnumerable<TEvent> Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadEventStream(TypedEventSchema);
}

public interface IListSchema
{
    IMemberSchema ElementMember { get; }

    Schema Element { get; }
}

public interface IListSchema<TCollection, TElement> : IListSchema
{
    Schema<TElement> ElementSchema { get; }

    IEnumerable<TElement> GetElements(TCollection value);

    /// <summary>Writes each element of <paramref name="value"/> as member 0.</summary>
    void SerializeElements<TSerializer>(TCollection value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;
}

public interface IListSchema<TCollection, TElement, TBuilder> : IListSchema<TCollection, TElement>
{
    TBuilder CreateTypedBuilder();

    void Add(TBuilder builder, TElement value);

    TCollection Build(TBuilder builder);

    /// <summary>Reads one element and adds it to <paramref name="builder"/>.</summary>
    void DeserializeElement<TDeserializer>(TBuilder builder, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public interface IMapSchema
{
    /// <summary>
    /// The key as the model declares it. A map key is always a string on the wire — that is what a
    /// JSON object name is — but the shape it targets can still say more about which strings are
    /// allowed, and an enum shape says exactly that. Keeping the shape rather than flattening it to
    /// <see cref="Schemas.String"/> is what lets a server hold a key to it.
    /// </summary>
    IMemberSchema KeyMember { get; }

    IMemberSchema ValueMember { get; }

    Schema Value { get; }
}

public interface IMapSchema<TDictionary, TValue> : IMapSchema
{
    Schema<TValue> ValueSchema { get; }

    IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value);

    /// <summary>Writes each entry of <paramref name="value"/>: its key as member 0, then its value as member 1.</summary>
    void SerializeEntries<TSerializer>(TDictionary value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;
}

public interface IMapSchema<TDictionary, TValue, TBuilder> : IMapSchema<TDictionary, TValue>
{
    TBuilder CreateTypedBuilder();

    void Add(TBuilder builder, string key, TValue value);

    TDictionary Build(TBuilder builder);

    /// <summary>Reads the value of the entry named <paramref name="key"/> into <paramref name="builder"/>.</summary>
    void DeserializeEntry<TDeserializer>(
        TBuilder builder,
        string key,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public interface IUnionCaseSchema
{
    ShapeId Id { get; }

    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    Schema Target { get; }
}

public interface IUnionSchema
{
    ShapeId Id { get; }

    IReadOnlyList<IUnionCaseSchema> Cases { get; }

    IUnionCaseSchema? GetCase(string name);

    /// <summary>The index of the case named <paramref name="name"/>, or -1.</summary>
    int IndexOf(string name);
}

public interface IUnionSchema<T> : IUnionSchema
{
    /// <summary>The index of the case <paramref name="value"/> holds.</summary>
    int CaseOf(T value);

    /// <summary>Writes the case <paramref name="value"/> holds under the case's index.</summary>
    void SerializeCase<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    /// <summary>Reads case <paramref name="index"/> and returns the union holding it.</summary>
    T DeserializeCase<TDeserializer>(int index, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

/// <summary>
/// A list or set. The C# collection is whatever the model's consumer wants it to be. Code
/// generation derives one class per list, which reads and writes the elements directly; this base
/// carries what every list has in common.
/// </summary>
public abstract class ListSchema<TCollection, TElement, TBuilder>
    : Schema<TCollection>,
        IListSchema<TCollection, TElement, TBuilder>
{
    protected ListSchema(
        ShapeId id,
        ShapeKind kind,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    )
        : base(id, kind, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
        ElementMember = new MemberSchema("member", element, isRequired: true, elementTraits).BindTo(
            id
        );
    }

    // An element is always present when the collection holds it; there is no absent case for a
    // consumer to check.
    public IMemberSchema ElementMember { get; }

    public Schema<TElement> ElementSchema { get; }

    public Schema Element => ElementSchema;

    public abstract IEnumerable<TElement> GetElements(TCollection value);

    public abstract TBuilder CreateTypedBuilder();

    public abstract void Add(TBuilder builder, TElement value);

    public abstract TCollection Build(TBuilder builder);

    public override void Write<TSerializer>(
        int member,
        TCollection value,
        ref TSerializer serializer
    )
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteList(member, value, this);
        }
    }

    public override TCollection Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadList(this);

    public abstract void SerializeElements<TSerializer>(
        TCollection value,
        ref TSerializer serializer
    )
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    public abstract void DeserializeElement<TDeserializer>(
        TBuilder builder,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

/// <summary>A list whose elements go through its element schema, as a hand-written schema builds one.</summary>
internal sealed class DelegateListSchema<TCollection, TElement, TBuilder>(
    ShapeId id,
    ShapeKind kind,
    Schema<TElement> element,
    Func<TCollection, IEnumerable<TElement>> getElements,
    Func<TBuilder> createBuilder,
    Action<TBuilder, TElement> add,
    Func<TBuilder, TCollection> build,
    IEnumerable<Trait>? traits = null,
    IEnumerable<Trait>? elementTraits = null
) : ListSchema<TCollection, TElement, TBuilder>(id, kind, element, traits, elementTraits)
{
    public override IEnumerable<TElement> GetElements(TCollection value) => getElements(value);

    public override TBuilder CreateTypedBuilder() => createBuilder();

    public override void Add(TBuilder builder, TElement value) => add(builder, value);

    public override TCollection Build(TBuilder builder) => build(builder);

    public override void SerializeElements<TSerializer>(
        TCollection value,
        ref TSerializer serializer
    )
    {
        // Indexed when possible: foreach over an IEnumerable boxes the collection's enumerator,
        // an allocation per list written.
        var elements = getElements(value);
        if (elements is IReadOnlyList<TElement> list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                ElementSchema.Write(0, list[index], ref serializer);
            }

            return;
        }

        foreach (var element in elements)
        {
            ElementSchema.Write(0, element, ref serializer);
        }
    }

    public override void DeserializeElement<TDeserializer>(
        TBuilder builder,
        ref TDeserializer deserializer
    ) => add(builder, ElementSchema.Read(ref deserializer));
}

/// <summary>
/// A map. Code generation derives one class per map, which reads and writes the entries directly;
/// this base carries what every map has in common.
/// </summary>
public abstract class MapSchema<TDictionary, TValue, TBuilder>
    : Schema<TDictionary>,
        IMapSchema<TDictionary, TValue, TBuilder>
{
    protected MapSchema(
        ShapeId id,
        Schema<TValue> value,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null,
        Schema? key = null
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        KeyMember = new MemberSchema(
            "key",
            key ?? Schemas.String,
            isRequired: true,
            keyTraits
        ).BindTo(id);
        ValueMember = new MemberSchema("value", value, isRequired: true, valueTraits).BindTo(id);
        ValueSchema = value;
    }

    public IMemberSchema KeyMember { get; }

    public IMemberSchema ValueMember { get; }

    public Schema<TValue> ValueSchema { get; }

    public Schema Value => ValueSchema;

    public abstract IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value);

    public abstract TBuilder CreateTypedBuilder();

    public abstract void Add(TBuilder builder, string key, TValue value);

    public abstract TDictionary Build(TBuilder builder);

    public override void Write<TSerializer>(
        int member,
        TDictionary value,
        ref TSerializer serializer
    )
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteMap(member, value, this);
        }
    }

    public override TDictionary Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadMap(this);

    public abstract void SerializeEntries<TSerializer>(
        TDictionary value,
        ref TSerializer serializer
    )
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    public abstract void DeserializeEntry<TDeserializer>(
        TBuilder builder,
        string key,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

/// <summary>A map whose values go through its value schema, as a hand-written schema builds one.</summary>
internal sealed class DelegateMapSchema<TDictionary, TValue, TBuilder>(
    ShapeId id,
    Schema<TValue> value,
    Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries,
    Func<TBuilder> createBuilder,
    Action<TBuilder, string, TValue> add,
    Func<TBuilder, TDictionary> build,
    IEnumerable<Trait>? traits = null,
    IEnumerable<Trait>? keyTraits = null,
    IEnumerable<Trait>? valueTraits = null,
    Schema? key = null
) : MapSchema<TDictionary, TValue, TBuilder>(id, value, traits, keyTraits, valueTraits, key)
{
    public override IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value) =>
        getEntries(value);

    public override TBuilder CreateTypedBuilder() => createBuilder();

    public override void Add(TBuilder builder, string key, TValue value) =>
        add(builder, key, value);

    public override TDictionary Build(TBuilder builder) => build(builder);

    public override void SerializeEntries<TSerializer>(
        TDictionary value,
        ref TSerializer serializer
    )
    {
        foreach (var (key, entry) in getEntries(value))
        {
            serializer.WriteString(0, key);
            ValueSchema.Write(1, entry, ref serializer);
        }
    }

    public override void DeserializeEntry<TDeserializer>(
        TBuilder builder,
        string key,
        ref TDeserializer deserializer
    ) => add(builder, key, ValueSchema.Read(ref deserializer));
}

/// <summary>
/// A case of a union: its name, the shape it holds, and the traits declared on it. The union gives
/// the case its id.
/// </summary>
public sealed class UnionCaseSchema : IUnionCaseSchema
{
    private ShapeId? id;

    public UnionCaseSchema(string name, Schema target, IEnumerable<Trait>? traits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);
        Name = name;
        Target = target;
        Traits = Trait.Index(traits);
    }

    public ShapeId Id =>
        id ?? throw new InvalidOperationException($"Union case '{Name}' belongs to no union.");

    public string Name { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Schema Target { get; }

    internal UnionCaseSchema BindTo(ShapeId union)
    {
        if (id is not null)
        {
            throw new InvalidOperationException($"Union case '{Name}' already belongs to '{id}'.");
        }

        id = union.WithMember(Name);
        return this;
    }
}

/// <summary>
/// A union. Code generation derives one class per union, which tells the cases apart and reads and
/// writes the value each holds; this base carries what every union has in common.
/// </summary>
public abstract class UnionSchema<T> : Schema<T>, IUnionSchema<T>
{
    private readonly UnionCaseSchema[] cases;
    private readonly Dictionary<string, IUnionCaseSchema> casesByName;

    protected UnionSchema(
        ShapeId id,
        IEnumerable<UnionCaseSchema> cases,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Union, traits)
    {
        ArgumentNullException.ThrowIfNull(cases);
        this.cases = [.. cases.Select(@case => @case.BindTo(id))];
        if (this.cases.Length == 0)
        {
            throw new ArgumentException($"Union schema '{id}' requires at least one case.");
        }

        casesByName = this.cases.ToDictionary(
            @case => @case.Name,
            @case => (IUnionCaseSchema)@case,
            StringComparer.Ordinal
        );
    }

    public IReadOnlyList<IUnionCaseSchema> Cases => cases;

    public IUnionCaseSchema? GetCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return casesByName.TryGetValue(name, out var @case) ? @case : null;
    }

    public int IndexOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var index = 0; index < cases.Length; index++)
        {
            if (string.Equals(cases[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public abstract int CaseOf(T value);

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer)
    {
        if (value is null)
        {
            serializer.WriteNull(member);
        }
        else
        {
            serializer.WriteUnion(member, value, this);
        }
    }

    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadUnion(this);

    public abstract void SerializeCase<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    public abstract T DeserializeCase<TDeserializer>(int index, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;

    /// <summary>The error for a value holding no modeled case, such as a variant this client does not know.</summary>
    protected static InvalidOperationException NoCaseMatched() =>
        new($"No union case matched '{typeof(T).Name}'.");
}

/// <summary>
/// A subset of a structure's members, as a protocol sees a structure once it has bound some members
/// elsewhere: the members it keeps are the ones the body codec reads and writes.
/// </summary>
public sealed class StructProjection<T, TBuilder>
{
    private readonly Dictionary<string, IMemberSchema> membersByName;

    internal StructProjection(IStructSchema<T, TBuilder> source, Func<IMemberSchema, bool> include)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(include);
        Source = source;
        membersByName = source
            .Members.Where(include)
            .ToDictionary(member => member.Name, StringComparer.Ordinal);
    }

    public IStructSchema<T, TBuilder> Source { get; }

    public IMemberSchema? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }
}

public interface IOperationSchema
{
    ShapeId Id { get; }

    ShapeKind Kind { get; }

    Schema Input { get; }

    Schema Output { get; }

    /// <summary>Whether the operation has a streaming input or output.</summary>
    bool IsStreaming { get; }

    IReadOnlyList<IOperationErrorSchema> Errors { get; }

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    Trait? GetTrait(ShapeId id);

    bool HasTrait(ShapeId id);

    /// <summary>Dispatches to <paramref name="visitor"/> with the input and output types in scope.</summary>
    TResult Accept<TResult>(IOperationSchemaVisitor<TResult> visitor);
}

/// <summary>
/// Recovers the input and output types hidden by <see cref="IOperationSchema"/> without reflection
/// or the runtime binder.
/// </summary>
public interface IOperationSchemaVisitor<out TResult>
{
    TResult Visit<TInput, TOutput>(OperationSchema<TInput, TOutput> schema);
}

public interface IOperationErrorSchema
{
    ShapeId Id { get; }

    Schema UntypedSchema { get; }

    int HttpStatusCode { get; }

    TResult Accept<TResult>(IOperationErrorSchemaVisitor<TResult> visitor);
}

/// <summary>Recovers a modeled error's exception type without runtime binder dispatch.</summary>
public interface IOperationErrorSchemaVisitor<out TResult>
{
    TResult Visit<TError>(OperationErrorSchema<TError> schema)
        where TError : Exception;
}

public sealed class OperationSchema<TInput, TOutput> : IOperationSchema
{
    internal OperationSchema(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<IOperationErrorSchema>? errors = null,
        IEnumerable<Trait>? traits = null,
        bool isStreaming = false
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        Id = id;
        Input = input;
        Output = output;
        Errors = WithImplicitValidationError(errors);
        Traits = Trait.Index(traits);
        IsStreaming = isStreaming;
    }

    public ShapeId Id { get; }

    public ShapeKind Kind => ShapeKind.Operation;

    public Schema<TInput> Input { get; }

    public Schema<TOutput> Output { get; }

    Schema IOperationSchema.Input => Input;

    Schema IOperationSchema.Output => Output;

    public bool IsStreaming { get; }

    public TResult Accept<TResult>(IOperationSchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }

    public IReadOnlyList<IOperationErrorSchema> Errors { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);

    // Every operation implicitly carries smithy.framework#ValidationException: the server runtime
    // returns it when input validation fails, and clients must be able to deserialize it. A model
    // that declares the error itself keeps its own registration.
    private static IOperationErrorSchema[] WithImplicitValidationError(
        IEnumerable<IOperationErrorSchema>? errors
    )
    {
        IOperationErrorSchema[] modeled = errors?.ToArray() ?? [];
        return modeled.Any(error => error.Id == ValidationExceptionSchema.Id)
            ? modeled
            : [.. modeled, ValidationExceptionSchema.OperationError];
    }
}

public sealed class OperationErrorSchema<TError>(
    ShapeId id,
    Schema<TError> schema,
    int httpStatusCode
) : IOperationErrorSchema
    where TError : Exception
{
    public ShapeId Id { get; } = id;

    public Schema<TError> Schema { get; } =
        schema ?? throw new ArgumentNullException(nameof(schema));

    public Schema UntypedSchema => Schema;

    public int HttpStatusCode { get; } = httpStatusCode;

    public TResult Accept<TResult>(IOperationErrorSchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }
}

/// <summary>
/// Runtime schema for a service shape. Unlike <see cref="OperationSchema{TInput, TOutput}"/> this
/// is deliberately service-scoped: it carries the service's shape id (so protocols can derive
/// service-named wire artifacts such as the rpcv2Cbor request path) and its service-level traits.
/// Operations remain service-agnostic and never reference a service.
/// </summary>
public sealed class ServiceSchema
{
    internal ServiceSchema(ShapeId id, IEnumerable<Trait>? traits = null)
        : this(id, string.Empty, traits) { }

    internal ServiceSchema(ShapeId id, string version, IEnumerable<Trait>? traits = null)
    {
        Id = id;
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Traits = Trait.Index(traits);
    }

    public ShapeId Id { get; }

    /// <summary>
    /// The service's Smithy <c>version</c>. Protocols such as AWS Query and EC2 Query serialize
    /// this value in every request. Manually constructed schemas that use the compatibility
    /// overload expose an empty version.
    /// </summary>
    public string Version { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);
}

public static class Schemas
{
    private const string PreludeNamespace = "smithy.api";

    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );

    private const string UnitShapeId = "smithy.api#Unit";

    /// <summary>
    /// Whether a schema is the empty structure Smithy synthesizes for an operation whose input or
    /// output is <c>smithy.api#Unit</c>. The synthetic shape carries
    /// <c>smithy.synthetic#originalShapeId</c> pointing back at the unit, which is the only thing
    /// telling it apart from a structure the model really declares with no members — a distinction
    /// protocols need, because one has no body and the other has an empty one.
    /// </summary>
    public static bool IsSyntheticUnit(Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema.GetTrait(SyntheticOriginalShapeId)?.Value is { Kind: DocumentKind.String } id
            && id.AsString() == UnitShapeId;
    }

    public static Schema<bool> Boolean { get; } =
        new BooleanSchema(new ShapeId(PreludeNamespace, "Boolean"));

    public static Schema<sbyte> Byte { get; } =
        new ByteSchema(new ShapeId(PreludeNamespace, "Byte"));

    public static Schema<short> Short { get; } =
        new ShortSchema(new ShapeId(PreludeNamespace, "Short"));

    public static Schema<int> Integer { get; } =
        new IntegerSchema(new ShapeId(PreludeNamespace, "Integer"));

    public static Schema<long> Long { get; } =
        new LongSchema(new ShapeId(PreludeNamespace, "Long"));

    public static Schema<float> Float { get; } =
        new FloatSchema(new ShapeId(PreludeNamespace, "Float"));

    public static Schema<double> Double { get; } =
        new DoubleSchema(new ShapeId(PreludeNamespace, "Double"));

    public static Schema<BigInteger> BigInteger { get; } =
        new BigIntegerSchema(new ShapeId(PreludeNamespace, "BigInteger"));

    public static Schema<decimal> BigDecimal { get; } =
        new BigDecimalSchema(new ShapeId(PreludeNamespace, "BigDecimal"));

    public static Schema<string> String { get; } =
        new StringSchema(new ShapeId(PreludeNamespace, "String"));

    public static Schema<byte[]> Blob { get; } =
        new BlobSchema(new ShapeId(PreludeNamespace, "Blob"));

    public static Schema<Stream> StreamingBlob { get; } =
        new StreamingBlobSchema(
            new ShapeId(PreludeNamespace, "Blob"),
            [new Trait(new ShapeId(PreludeNamespace, "streaming"))]
        );

    public static Schema<DateTimeOffset> Timestamp { get; } =
        new TimestampSchema(new ShapeId(PreludeNamespace, "Timestamp"));

    /// <summary>
    /// A timestamp schema carrying traits (e.g. <c>@timestampFormat</c>) so codecs can resolve the
    /// wire format from the schema rather than a shared singleton.
    /// </summary>
    public static Schema<DateTimeOffset> TimestampWithTraits(IEnumerable<Trait>? traits) =>
        new TimestampSchema(new ShapeId(PreludeNamespace, "Timestamp"), traits);

    public static Schema<Document> Document { get; } =
        new DocumentSchema(new ShapeId(PreludeNamespace, "Document"));

    public static Schema<SmithyUnit> Unit { get; } = new UnitSchema();

    public static Schema<T> Lazy<T>(Func<Schema<T>> resolve) => new LazySchema<T>(resolve);

    public static Schema<T?> Nullable<T>(Schema<T> target)
        where T : struct => new NullableSchema<T>(target);

    public static Schema<T?> NullableReference<T>(Schema<T> target)
        where T : class => (Schema<T?>)(object)target;

    public static EventStreamSchema<TEvent> EventStream<TEvent>(Schema<TEvent> eventSchema) =>
        new(eventSchema);

    public static StringEnumSchema<T> StringEnum<T>(
        ShapeId id,
        IEnumerable<string>? values = null,
        IEnumerable<Trait>? traits = null,
        IEnumerable<string>? internalValues = null
    )
        where T : IStringEnumValue<T> => new(id, values, traits, internalValues);

    public static IntEnumSchema<T> IntEnum<T>(
        ShapeId id,
        IEnumerable<int>? values = null,
        IEnumerable<Trait>? traits = null
    )
        where T : struct, Enum => new(id, values, traits);

    /// <summary>A list read as <see cref="IReadOnlyList{T}"/>; the form a hand-written schema wants.</summary>
    public static ListSchema<IReadOnlyList<TElement>, TElement, List<TElement>> List<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new DelegateListSchema<IReadOnlyList<TElement>, TElement, List<TElement>>(
            id,
            ShapeKind.List,
            element,
            static value => value,
            static () => [],
            static (builder, value) => builder.Add(value),
            static builder => new ReadOnlyCollection<TElement>(builder.ToArray()),
            traits,
            elementTraits
        );

    public static ListSchema<TCollection, TElement, TBuilder> List<TCollection, TElement, TBuilder>(
        ShapeId id,
        Schema<TElement> element,
        Func<TCollection, IEnumerable<TElement>> getElements,
        Func<TBuilder> createBuilder,
        Action<TBuilder, TElement> add,
        Func<TBuilder, TCollection> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new DelegateListSchema<TCollection, TElement, TBuilder>(
            id,
            ShapeKind.List,
            element,
            getElements,
            createBuilder,
            add,
            build,
            traits,
            elementTraits
        );

    public static ListSchema<IReadOnlySet<TElement>, TElement, HashSet<TElement>> Set<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new DelegateListSchema<IReadOnlySet<TElement>, TElement, HashSet<TElement>>(
            id,
            ShapeKind.Set,
            element,
            static value => value,
            static () => [],
            static (builder, value) => builder.Add(value),
            static builder => builder,
            traits,
            elementTraits
        );

    public static ListSchema<TCollection, TElement, TBuilder> Set<TCollection, TElement, TBuilder>(
        ShapeId id,
        Schema<TElement> element,
        Func<TCollection, IEnumerable<TElement>> getElements,
        Func<TBuilder> createBuilder,
        Action<TBuilder, TElement> add,
        Func<TBuilder, TCollection> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new DelegateListSchema<TCollection, TElement, TBuilder>(
            id,
            ShapeKind.Set,
            element,
            getElements,
            createBuilder,
            add,
            build,
            traits,
            elementTraits
        );

    /// <param name="key">
    /// The shape the key targets. Defaults to <see cref="String"/>, which is what a map key with no
    /// shape of its own is; pass the modeled shape when the model gives it one, so a server can hold
    /// the key to what that shape says.
    /// </param>
    public static MapSchema<
        IReadOnlyDictionary<string, TValue>,
        TValue,
        Dictionary<string, TValue>
    > Map<TValue>(
        ShapeId id,
        Schema<TValue> value,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null,
        Schema? key = null
    ) =>
        new DelegateMapSchema<
            IReadOnlyDictionary<string, TValue>,
            TValue,
            Dictionary<string, TValue>
        >(
            id,
            value,
            static value => value,
            static () => new Dictionary<string, TValue>(StringComparer.Ordinal),
            static (builder, entryKey, entryValue) => builder.Add(entryKey, entryValue),
            static builder => new ReadOnlyDictionary<string, TValue>(builder),
            traits,
            keyTraits,
            valueTraits,
            key
        );

    public static MapSchema<TDictionary, TValue, TBuilder> Map<TDictionary, TValue, TBuilder>(
        ShapeId id,
        Schema<TValue> value,
        Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries,
        Func<TBuilder> createBuilder,
        Action<TBuilder, string, TValue> add,
        Func<TBuilder, TDictionary> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null,
        Schema? key = null
    ) =>
        new DelegateMapSchema<TDictionary, TValue, TBuilder>(
            id,
            value,
            getEntries,
            createBuilder,
            add,
            build,
            traits,
            keyTraits,
            valueTraits,
            key
        );

    public static OperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<Trait>? traits = null,
        bool isStreaming = false
    ) => new(id, input, output, null, traits, isStreaming);

    public static OperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<IOperationErrorSchema>? errors,
        IEnumerable<Trait>? traits = null,
        bool isStreaming = false
    ) => new(id, input, output, errors, traits, isStreaming);

    public static OperationErrorSchema<TError> OperationError<TError>(
        ShapeId id,
        Schema<TError> schema,
        int httpStatusCode
    )
        where TError : Exception => new(id, schema, httpStatusCode);

    public static ServiceSchema Service(ShapeId id, IEnumerable<Trait>? traits = null) =>
        new(id, traits);

    public static ServiceSchema Service(
        ShapeId id,
        string version,
        IEnumerable<Trait>? traits = null
    ) => new(id, version, traits);

    /// <summary>The members of <paramref name="source"/> for which <paramref name="include"/> holds.</summary>
    public static StructProjection<T, TBuilder> Project<T, TBuilder>(
        IStructSchema<T, TBuilder> source,
        Func<IMemberSchema, bool> include
    ) => new(source, include);

    /// <summary>The members of <paramref name="source"/> named in <paramref name="memberNames"/>.</summary>
    public static StructProjection<T, TBuilder> Project<T, TBuilder>(
        IStructSchema<T, TBuilder> source,
        IReadOnlySet<string> memberNames
    )
    {
        ArgumentNullException.ThrowIfNull(memberNames);
        return new(source, member => memberNames.Contains(member.Name));
    }

    /// <summary>
    /// Compiles the function that names the case a union value holds, e.g. for an event stream's
    /// <c>:event-type</c>.
    /// </summary>
    public static Func<TUnion, string> CompileCaseName<TUnion>(IUnionSchema<TUnion> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var names = schema.Cases.Select(@case => @case.Name).ToArray();
        return value => names[schema.CaseOf(value)];
    }
}

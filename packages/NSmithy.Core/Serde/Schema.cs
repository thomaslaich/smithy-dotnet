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

    public abstract TResult Accept<TResult>(ISchemaVisitor<TResult> visitor);

    public bool IsMember => Id.IsMember;

    public string? MemberName => Id.MemberName;

    public virtual Trait? GetTrait(ShapeId id) =>
        Traits.TryGetValue(id, out var trait) ? trait : null;

    public virtual bool HasTrait(ShapeId id) => Traits.ContainsKey(id);

    /// <summary>
    /// Writes <paramref name="value"/>, the Smithy document form of a value of this shape such as a
    /// modeled <c>@default</c>, as member <paramref name="member"/> of the shape being serialized.
    /// </summary>
    public abstract void WriteDocumentValue<TSerializer>(
        int member,
        Document value,
        ref TSerializer serializer
    )
        where TSerializer : struct, IShapeSerializer, allows ref struct;
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

    public override void WriteDocumentValue<TSerializer>(
        int member,
        Document value,
        ref TSerializer serializer
    )
    {
        var deserializer = new DocumentDeserializer(value);
        Write(member, Read(ref deserializer), ref serializer);
    }
}

public interface ISchemaVisitor<out TResult>
{
    TResult VisitBoolean(Schema<bool> schema);

    TResult VisitByte(Schema<sbyte> schema);

    TResult VisitShort(Schema<short> schema);

    TResult VisitInteger(Schema<int> schema);

    TResult VisitLong(Schema<long> schema);

    TResult VisitFloat(Schema<float> schema);

    TResult VisitDouble(Schema<double> schema);

    TResult VisitBigInteger(Schema<BigInteger> schema);

    TResult VisitBigDecimal(Schema<decimal> schema);

    TResult VisitString(Schema<string> schema);

    TResult VisitBlob(Schema<byte[]> schema);

    /// <summary>
    /// A <c>@streaming</c> blob, whose value is an unread <see cref="Stream"/> rather than a
    /// buffered payload. Most consumers cannot handle one — but each says so itself, rather than
    /// the schema refusing to be visited at all.
    /// </summary>
    TResult VisitStreamingBlob(Schema<Stream> schema);

    TResult VisitTimestamp(Schema<DateTimeOffset> schema);

    TResult VisitDocument(Schema<Document> schema);

    TResult VisitNullable<T>(NullableSchema<T> schema)
        where T : struct;

    TResult VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema);

    TResult VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    );

    TResult VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    );

    TResult VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema);

    TResult VisitUnion<T>(IUnionSchema<T> schema);

    TResult VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T>;

    TResult VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum;
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

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return TargetSchema.Accept(visitor);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBoolean(this);
    }

    public override void Write<TSerializer>(int member, bool value, ref TSerializer serializer) =>
        serializer.WriteBoolean(member, value);

    public override bool Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadBoolean();
}

public sealed class ByteSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<sbyte>(id, ShapeKind.Byte, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitByte(this);
    }

    public override void Write<TSerializer>(int member, sbyte value, ref TSerializer serializer) =>
        serializer.WriteByte(member, value);

    public override sbyte Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadByte();
}

public sealed class ShortSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<short>(id, ShapeKind.Short, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitShort(this);
    }

    public override void Write<TSerializer>(int member, short value, ref TSerializer serializer) =>
        serializer.WriteShort(member, value);

    public override short Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadShort();
}

public sealed class IntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<int>(id, ShapeKind.Integer, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitInteger(this);
    }

    public override void Write<TSerializer>(int member, int value, ref TSerializer serializer) =>
        serializer.WriteInteger(member, value);

    public override int Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadInteger();
}

public sealed class LongSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<long>(id, ShapeKind.Long, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitLong(this);
    }

    public override void Write<TSerializer>(int member, long value, ref TSerializer serializer) =>
        serializer.WriteLong(member, value);

    public override long Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadLong();
}

public sealed class FloatSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<float>(id, ShapeKind.Float, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitFloat(this);
    }

    public override void Write<TSerializer>(int member, float value, ref TSerializer serializer) =>
        serializer.WriteFloat(member, value);

    public override float Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadFloat();
}

public sealed class DoubleSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<double>(id, ShapeKind.Double, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitDouble(this);
    }

    public override void Write<TSerializer>(int member, double value, ref TSerializer serializer) =>
        serializer.WriteDouble(member, value);

    public override double Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.ReadDouble();
}

public sealed class BigIntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<BigInteger>(id, ShapeKind.BigInteger, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBigInteger(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBigDecimal(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitString(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBlob(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStreamingBlob(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitTimestamp(this);
    }

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
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitDocument(this);
    }

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

public interface ITypedTargetMemberSchema<TValue> : IMemberSchema
{
    Schema<TValue> TypedTarget { get; }
}

public interface IMemberSchema<TContainer> : IMemberSchema
{
    void Accept(IMemberVisitor<TContainer> visitor);
}

public interface IMemberSchema<TContainer, TValue>
    : IMemberSchema<TContainer>,
        ITypedTargetMemberSchema<TValue>
{
    TValue GetValue(TContainer container);
}

/// <summary>
/// A member whose builder type is known while its value type is erased, which is what a reader
/// needs to dispatch without reflection or the runtime binder.
/// </summary>
public interface IBuilderMemberSchema<TContainer, TBuilder> : IMemberSchema<TContainer>
{
    void Accept(IMemberVisitor<TContainer, TBuilder> visitor);
}

public interface IMemberSchema<TContainer, TBuilder, TValue>
    : IMemberSchema<TContainer, TValue>,
        IBuilderMemberSchema<TContainer, TBuilder>
{
    void SetValue(TBuilder builder, TValue value);
}

public interface IMemberVisitor<TContainer>
{
    void Visit<TValue>(IMemberSchema<TContainer, TValue> member);
}

public interface IMemberVisitor<TContainer, TBuilder>
{
    void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member);
}

/// <summary>
/// Receives statically typed structure members during serialization. Member indexes follow the
/// structure schema's declaration order.
/// </summary>
public interface IStructMemberWriter
{
    void WriteMember<TValue>(int index, TValue value);
}

/// <summary>
/// Supplies a structure's values directly to a wire-format serializer without using member getter
/// delegates.
/// </summary>
public interface IStructValueSerializer<T>
{
    void WriteMembers<TWriter>(T value, ref TWriter writer)
        where TWriter : struct, IStructMemberWriter;
}

internal interface IMemberValueSource<in TContainer>
{
    void WriteMember<TWriter>(TContainer container, int index, ref TWriter writer)
        where TWriter : struct, IStructMemberWriter;
}

/// <summary>Moves one member's value between a container and a shape serializer.</summary>
internal interface IMemberSerialization<in TContainer, in TBuilder>
{
    void Serialize<TSerializer>(int index, TContainer container, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    void Deserialize<TDeserializer>(TBuilder builder, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
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
    /// Writes this structure's member values in declaration order. Every structure has one: a
    /// generated schema supplies a serializer that reads its properties directly, and a schema built
    /// without one reads them through the member getters.
    /// </summary>
    IStructValueSerializer<T> ValueSerializer { get; }

    /// <summary>
    /// Dispatches to <paramref name="visitor"/> with this structure's otherwise hidden builder type.
    /// </summary>
    TResult Accept<TResult>(IStructSchemaVisitor<T, TResult> visitor);

    void VisitMembers(IMemberVisitor<T> visitor);

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

    void VisitMembers(IMemberVisitor<T, TBuilder> visitor);

    /// <summary>Reads member <paramref name="index"/> into <paramref name="builder"/>.</summary>
    void DeserializeMember<TDeserializer>(
        TBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public sealed class StructSchema<T, TBuilder> : Schema<T>, IStructSchema<T, TBuilder>
{
    private readonly Func<TBuilder> createBuilder;
    private readonly Func<TBuilder, T> build;
    private readonly IBuilderMemberSchema<T, TBuilder>[] members;
    private readonly Dictionary<string, IMemberSchema> membersByName;

    internal StructSchema(
        ShapeId id,
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IReadOnlyList<IBuilderMemberSchema<T, TBuilder>> members,
        IEnumerable<Trait>? traits = null,
        IStructValueSerializer<T>? valueSerializer = null
    )
        : base(id, ShapeKind.Structure, traits)
    {
        this.createBuilder = createBuilder;
        this.build = build;
        this.members = [.. members];
        membersByName = BuildMembersByName(this.members);
        ValueSerializer = valueSerializer ?? new MemberGetterValueSerializer(this.members);
    }

    public IStructValueSerializer<T> ValueSerializer { get; }

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

    public void SerializeMembers<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct
    {
        for (var index = 0; index < members.Length; index++)
        {
            ((IMemberSerialization<T, TBuilder>)members[index]).Serialize(
                index,
                value,
                ref serializer
            );
        }
    }

    public void DeserializeMember<TDeserializer>(
        TBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct =>
        ((IMemberSerialization<T, TBuilder>)members[index]).Deserialize(builder, ref deserializer);

    public TBuilder CreateTypedBuilder() => createBuilder();

    public T Build(TBuilder builder) => build(builder);

    public T BuildEmpty() => build(createBuilder());

    public void WriteEmpty<TSerializer>(int member, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct =>
        Write(member, BuildEmpty(), ref serializer);

    public TResult Accept<TResult>(IStructSchemaVisitor<T, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }

    public void VisitMembers(IMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in members)
        {
            member.Accept(visitor);
        }
    }

    public void VisitMembers(IMemberVisitor<T, TBuilder> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in members)
        {
            member.Accept(visitor);
        }
    }

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStruct(this);
    }

    private sealed class MemberGetterValueSerializer(IBuilderMemberSchema<T, TBuilder>[] members)
        : IStructValueSerializer<T>
    {
        // Every member a StructSchemaBuilder adds is a MemberSchema, which can hand its own value
        // to a writer without the caller knowing its value type.
        private readonly IMemberValueSource<T>[] sources =
        [
            .. members.Select(static member => (IMemberValueSource<T>)member),
        ];

        public void WriteMembers<TWriter>(T value, ref TWriter writer)
            where TWriter : struct, IStructMemberWriter
        {
            for (var index = 0; index < sources.Length; index++)
            {
                sources[index].WriteMember(value, index, ref writer);
            }
        }
    }

    private static Dictionary<string, IMemberSchema> BuildMembersByName(
        IBuilderMemberSchema<T, TBuilder>[] members
    )
    {
        var byName = new Dictionary<string, IMemberSchema>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            byName.Add(member.Name, member);
        }

        return byName;
    }
}

public sealed class UnitSchema : Schema<SmithyUnit>, IStructSchema<SmithyUnit, SmithyUnit>
{
    internal UnitSchema()
        : base(new ShapeId("smithy.api", "Unit"), ShapeKind.Structure) { }

    public IStructValueSerializer<SmithyUnit> ValueSerializer { get; } = new NoMembers();

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

    public void VisitMembers(IMemberVisitor<SmithyUnit> visitor) { }

    public void VisitMembers(IMemberVisitor<SmithyUnit, SmithyUnit> visitor) { }

    public SmithyUnit CreateTypedBuilder() => SmithyUnit.Value;

    public SmithyUnit Build(SmithyUnit builder) => SmithyUnit.Value;

    public SmithyUnit BuildEmpty() => SmithyUnit.Value;

    public void WriteEmpty<TSerializer>(int member, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct =>
        Write(member, SmithyUnit.Value, ref serializer);

    private sealed class NoMembers : IStructValueSerializer<SmithyUnit>
    {
        public void WriteMembers<TWriter>(SmithyUnit value, ref TWriter writer)
            where TWriter : struct, IStructMemberWriter { }
    }

    public TResult Accept<TResult>(IStructSchemaVisitor<SmithyUnit, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.Visit(this);
    }

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStruct(this);
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

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitNullable(this);
    }

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

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStringEnum(this);
    }

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

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitIntEnum(this);
    }

    public override void Write<TSerializer>(int member, T value, ref TSerializer serializer) =>
        serializer.WriteIntEnum(member, GetIntegerValue(value));

    public override T Read<TDeserializer>(ref TDeserializer deserializer) =>
        Create(deserializer.ReadIntEnum());
}

public interface IEventStreamSchema
{
    Schema EventSchema { get; }
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

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitEventStream(this);
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
    ITypedTargetMemberSchema<TElement> TypedElementMember { get; }

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
    ITypedTargetMemberSchema<TValue> TypedValueMember { get; }

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

public interface IUnionCaseSchema<TUnion, TValue> : IUnionCaseSchema
{
    Schema<TValue> TargetSchema { get; }

    bool Matches(TUnion value);

    TValue GetValue(TUnion value);

    TUnion Create(TValue value);
}

public interface IUnionCaseVisitor<TUnion>
{
    void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase);
}

internal interface IUnionCaseSchema<TUnion>
{
    void Accept(IUnionCaseVisitor<TUnion> visitor);

    bool Matches(TUnion value);

    bool TrySerialize<TSerializer>(int index, TUnion value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    TUnion Deserialize<TDeserializer>(ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
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
    void VisitCases(IUnionCaseVisitor<T> visitor);

    /// <summary>The index of the case <paramref name="value"/> holds.</summary>
    int CaseOf(T value);

    /// <summary>Writes the case <paramref name="value"/> holds under the case's index.</summary>
    void SerializeCase<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    /// <summary>Reads case <paramref name="index"/> and returns the union holding it.</summary>
    T DeserializeCase<TDeserializer>(int index, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public sealed class CollectionMemberSchema<TValue> : ITypedTargetMemberSchema<TValue>
{
    private readonly IReadOnlyDictionary<ShapeId, Trait> memberTraits;

    internal CollectionMemberSchema(
        ShapeId id,
        Schema<TValue> target,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!id.IsMember)
        {
            throw new ArgumentException(
                $"Member schema id must include a member name; got '{id}'.",
                nameof(id)
            );
        }

        Id = id;
        TypedTarget = target;
        memberTraits = Trait.Index(traits);
    }

    public ShapeId Id { get; }

    public string Name => Id.MemberName!;

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits => memberTraits;

    public Schema<TValue> TypedTarget { get; }

    public Schema Target => TypedTarget;

    // A collection element or map entry is always present when the collection holds it; there is
    // no absent case for a consumer to check.
    public bool IsRequired => true;
}

/// <summary>
/// A map's key member. Untyped, unlike every other member, because a key's CLR type and its modeled
/// shape need not agree: the key of a map is a <see cref="string"/> whatever it targets — a JSON
/// object name has no other form — while the shape it targets may be an enum, whose own CLR type is
/// the generated enum. Carrying the shape is what lets a server hold the key to it.
/// </summary>
public sealed class MapKeyMemberSchema : IMemberSchema
{
    private readonly IReadOnlyDictionary<ShapeId, Trait> memberTraits;

    internal MapKeyMemberSchema(ShapeId id, Schema target, IEnumerable<Trait>? traits = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!id.IsMember)
        {
            throw new ArgumentException(
                $"Member schema id must include a member name; got '{id}'.",
                nameof(id)
            );
        }

        Id = id;
        Target = target;
        memberTraits = Trait.Index(traits);
    }

    public ShapeId Id { get; }

    public string Name => Id.MemberName!;

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits => memberTraits;

    public Schema Target { get; }

    // An entry's key is always present when the map holds the entry.
    public bool IsRequired => true;
}

/// <summary>
/// A list or set. The C# collection is whatever the model's consumer wants it to be; the schema
/// carries the four operations a codec needs to read one and enumerate one.
/// </summary>
public sealed class CollectionSchema<TCollection, TElement, TBuilder>
    : Schema<TCollection>,
        IListSchema<TCollection, TElement, TBuilder>
{
    private readonly Func<TCollection, IEnumerable<TElement>> getElements;
    private readonly Func<TBuilder> createBuilder;
    private readonly Action<TBuilder, TElement> add;
    private readonly Func<TBuilder, TCollection> build;

    internal CollectionSchema(
        ShapeId id,
        ShapeKind kind,
        Schema<TElement> element,
        Func<TCollection, IEnumerable<TElement>> getElements,
        Func<TBuilder> createBuilder,
        Action<TBuilder, TElement> add,
        Func<TBuilder, TCollection> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    )
        : base(id, kind, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(getElements);
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(build);

        ElementSchema = element;
        TypedElementMember = new CollectionMemberSchema<TElement>(
            id.WithMember("member"),
            element,
            elementTraits
        );
        this.getElements = getElements;
        this.createBuilder = createBuilder;
        this.add = add;
        this.build = build;
    }

    public ITypedTargetMemberSchema<TElement> TypedElementMember { get; }

    public IMemberSchema ElementMember => TypedElementMember;

    public Schema<TElement> ElementSchema { get; }

    public Schema Element => ElementSchema;

    public IEnumerable<TElement> GetElements(TCollection value) => getElements(value);

    public TBuilder CreateTypedBuilder() => createBuilder();

    public void Add(TBuilder builder, TElement value) => add(builder, value);

    public TCollection Build(TBuilder builder) => build(builder);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitList(this);
    }

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

    public void SerializeElements<TSerializer>(TCollection value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct
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

    public void DeserializeElement<TDeserializer>(TBuilder builder, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct =>
        add(builder, ElementSchema.Read(ref deserializer));
}

public sealed class MapSchema<TDictionary, TValue, TBuilder>
    : Schema<TDictionary>,
        IMapSchema<TDictionary, TValue, TBuilder>
{
    private readonly Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries;
    private readonly Func<TBuilder> createBuilder;
    private readonly Action<TBuilder, string, TValue> add;
    private readonly Func<TBuilder, TDictionary> build;

    internal MapSchema(
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
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(getEntries);
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(build);

        KeyMember = new MapKeyMemberSchema(id.WithMember("key"), key ?? Schemas.String, keyTraits);
        TypedValueMember = new CollectionMemberSchema<TValue>(
            id.WithMember("value"),
            value,
            valueTraits
        );
        ValueSchema = value;
        this.getEntries = getEntries;
        this.createBuilder = createBuilder;
        this.add = add;
        this.build = build;
    }

    public IMemberSchema KeyMember { get; }

    public ITypedTargetMemberSchema<TValue> TypedValueMember { get; }

    public IMemberSchema ValueMember => TypedValueMember;

    public Schema<TValue> ValueSchema { get; }

    public Schema Value => ValueSchema;

    public IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value) =>
        getEntries(value);

    public TBuilder CreateTypedBuilder() => createBuilder();

    public void Add(TBuilder builder, string key, TValue value) => add(builder, key, value);

    public TDictionary Build(TBuilder builder) => build(builder);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitMap(this);
    }

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

    public void SerializeEntries<TSerializer>(TDictionary value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct
    {
        foreach (var (key, entry) in getEntries(value))
        {
            serializer.WriteString(0, key);
            ValueSchema.Write(1, entry, ref serializer);
        }
    }

    public void DeserializeEntry<TDeserializer>(
        TBuilder builder,
        string key,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct =>
        add(builder, key, ValueSchema.Read(ref deserializer));
}

public sealed class UnionCaseSchema<TUnion, TValue>
    : IUnionCaseSchema<TUnion, TValue>,
        IUnionCaseSchema<TUnion>
{
    private readonly Func<TUnion, bool> matches;
    private readonly Func<TUnion, TValue> get;
    private readonly Func<TValue, TUnion> create;

    internal UnionCaseSchema(
        ShapeId id,
        Schema<TValue> target,
        Func<TUnion, bool> matches,
        Func<TUnion, TValue> get,
        Func<TValue, TUnion> create,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!id.IsMember)
        {
            throw new ArgumentException(
                $"Union case id must include a member name; got '{id}'.",
                nameof(id)
            );
        }

        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(create);

        Id = id;
        Traits = Trait.Index(traits);
        TargetSchema = target;
        this.matches = matches;
        this.get = get;
        this.create = create;
    }

    public ShapeId Id { get; }

    public string Name => Id.MemberName!;

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Schema<TValue> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public bool Matches(TUnion value) => matches(value);

    public TValue GetValue(TUnion value) => get(value);

    public TUnion Create(TValue value) => create(value);

    public void Accept(IUnionCaseVisitor<TUnion> visitor) => visitor.Visit(this);

    public bool TrySerialize<TSerializer>(int index, TUnion value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct
    {
        if (!matches(value))
        {
            return false;
        }

        TargetSchema.Write(index, get(value), ref serializer);
        return true;
    }

    public TUnion Deserialize<TDeserializer>(ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct =>
        create(TargetSchema.Read(ref deserializer));
}

public sealed class UnionSchema<T> : Schema<T>, IUnionSchema<T>
{
    private readonly ReadOnlyCollection<IUnionCaseSchema> cases;
    private readonly Dictionary<string, IUnionCaseSchema> casesByName;

    internal UnionSchema(
        ShapeId id,
        IReadOnlyList<IUnionCaseSchema> cases,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Union, traits)
    {
        this.cases = new ReadOnlyCollection<IUnionCaseSchema>(cases.ToArray());
        casesByName = BuildCasesByName(this.cases);
    }

    public IReadOnlyList<IUnionCaseSchema> Cases => cases;

    public IUnionCaseSchema? GetCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return casesByName.TryGetValue(name, out var @case) ? @case : null;
    }

    public void VisitCases(IUnionCaseVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var @case in cases)
        {
            ((IUnionCaseSchema<T>)@case).Accept(visitor);
        }
    }

    public int IndexOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var index = 0; index < cases.Count; index++)
        {
            if (string.Equals(cases[index].Name, name, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public int CaseOf(T value)
    {
        for (var index = 0; index < cases.Count; index++)
        {
            if (((IUnionCaseSchema<T>)cases[index]).Matches(value))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }

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

    public void SerializeCase<TSerializer>(T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct
    {
        for (var index = 0; index < cases.Count; index++)
        {
            if (((IUnionCaseSchema<T>)cases[index]).TrySerialize(index, value, ref serializer))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }

    public T DeserializeCase<TDeserializer>(int index, ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct =>
        ((IUnionCaseSchema<T>)cases[index]).Deserialize(ref deserializer);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitUnion(this);
    }

    private static Dictionary<string, IUnionCaseSchema> BuildCasesByName(
        ReadOnlyCollection<IUnionCaseSchema> cases
    )
    {
        var byName = new Dictionary<string, IUnionCaseSchema>(StringComparer.Ordinal);
        foreach (var @case in cases)
        {
            byName.Add(@case.Name, @case);
        }

        return byName;
    }
}

public sealed class MemberSchema<TContainer, TBuilder, TValue>
    : Schema<TValue>,
        IMemberSchema<TContainer, TBuilder, TValue>,
        IMemberValueSource<TContainer>,
        IMemberSerialization<TContainer, TBuilder>
{
    private readonly Func<TContainer, TValue> get;
    private readonly Action<TBuilder, TValue> set;

    internal MemberSchema(
        ShapeId id,
        bool isRequired,
        Schema<TValue> target,
        Func<TContainer, TValue> get,
        Action<TBuilder, TValue> set,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Member, traits)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!id.IsMember)
        {
            throw new ArgumentException(
                $"Member schema id must include a member name; got '{id}'.",
                nameof(id)
            );
        }

        IsRequired = isRequired;
        TypedTarget = target;
        this.get = get;
        this.set = set;
    }

    public string Name => MemberName!;

    public bool IsRequired { get; }

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits => base.Traits;

    public Schema<TValue> TypedTarget { get; }

    public Schema Target => TypedTarget;

    public TValue GetValue(TContainer container) => get(container);

    void IMemberValueSource<TContainer>.WriteMember<TWriter>(
        TContainer container,
        int index,
        ref TWriter writer
    ) => writer.WriteMember(index, get(container));

    public void Set(TBuilder builder, TValue value) => set(builder, value);

    void IMemberSerialization<TContainer, TBuilder>.Serialize<TSerializer>(
        int index,
        TContainer container,
        ref TSerializer serializer
    ) => TypedTarget.Write(index, get(container), ref serializer);

    void IMemberSerialization<TContainer, TBuilder>.Deserialize<TDeserializer>(
        TBuilder builder,
        ref TDeserializer deserializer
    ) => set(builder, TypedTarget.Read(ref deserializer));

    public override void Write<TSerializer>(int member, TValue value, ref TSerializer serializer) =>
        TypedTarget.Write(member, value, ref serializer);

    public override TValue Read<TDeserializer>(ref TDeserializer deserializer) =>
        TypedTarget.Read(ref deserializer);

    public void SetValue(TBuilder builder, TValue value) => set(builder, value);

    public void Accept(IMemberVisitor<TContainer> visitor) => visitor.Visit(this);

    public void Accept(IMemberVisitor<TContainer, TBuilder> visitor) => visitor.Visit(this);

    public override Trait? GetTrait(ShapeId id) =>
        MemberTraits.TryGetValue(id, out var trait) ? trait : TypedTarget.GetTrait(id);

    public override bool HasTrait(ShapeId id) => GetTrait(id) is not null;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return TypedTarget.Accept(visitor);
    }
}

/// <summary>
/// A subset of a structure's members, as a protocol sees a structure once it has bound some members
/// elsewhere: the members it keeps are the ones the body codec reads and writes.
/// </summary>
public sealed class StructProjection<T, TBuilder>
{
    private readonly IBuilderMemberSchema<T, TBuilder>[] members;
    private readonly Dictionary<string, IMemberSchema> membersByName;

    internal StructProjection(IStructSchema<T, TBuilder> source, Func<IMemberSchema, bool> include)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(include);
        Source = source;
        var collector = new MemberCollector(include);
        source.VisitMembers(collector);
        members = [.. collector.Members];
        membersByName = members.ToDictionary(
            member => member.Name,
            member => (IMemberSchema)member,
            StringComparer.Ordinal
        );
    }

    public IStructSchema<T, TBuilder> Source { get; }

    public IMemberSchema? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public void VisitMembers(IMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in members)
        {
            member.Accept(visitor);
        }
    }

    public void VisitMembers(IMemberVisitor<T, TBuilder> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in members)
        {
            member.Accept(visitor);
        }
    }

    private sealed class MemberCollector(Func<IMemberSchema, bool> include)
        : IMemberVisitor<T, TBuilder>
    {
        public List<IBuilderMemberSchema<T, TBuilder>> Members { get; } = [];

        public void Visit<TValue>(IMemberSchema<T, TBuilder, TValue> member)
        {
            if (include(member))
            {
                Members.Add(member);
            }
        }
    }
}

public sealed class StructSchemaBuilder<T, TBuilder>
{
    private readonly ShapeId id;
    private readonly IReadOnlyList<Trait>? traits;
    private readonly List<IBuilderMemberSchema<T, TBuilder>> members = [];

    internal StructSchemaBuilder(ShapeId id, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.traits = traits?.ToArray();
    }

    public StructSchemaBuilder<T, TBuilder> Required<TValue>(
        string name,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        Schema<TValue> target,
        IEnumerable<Trait>? traits = null
    ) => Member(name, isRequired: true, get, set, target, traits);

    public StructSchemaBuilder<T, TBuilder> Optional<TValue>(
        string name,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        Schema<TValue> target,
        IEnumerable<Trait>? traits = null
    ) => Member(name, isRequired: false, get, set, target, traits);

    public StructSchema<T, TBuilder> Build(
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IStructValueSerializer<T>? valueSerializer = null
    )
    {
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(build);

        return new StructSchema<T, TBuilder>(
            id,
            createBuilder,
            build,
            members,
            traits,
            valueSerializer
        );
    }

    private StructSchemaBuilder<T, TBuilder> Member<TValue>(
        string name,
        bool isRequired,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        Schema<TValue> target,
        IEnumerable<Trait>? traits
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(target);

        members.Add(
            new MemberSchema<T, TBuilder, TValue>(
                id.WithMember(name),
                isRequired,
                target,
                get,
                set,
                traits
            )
        );
        return this;
    }
}

public sealed class UnionSchemaBuilder<T>
{
    private readonly ShapeId id;
    private readonly IReadOnlyList<Trait>? traits;
    private readonly List<IUnionCaseSchema> cases = [];

    internal UnionSchemaBuilder(ShapeId id, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.traits = traits?.ToArray();
    }

    public UnionSchemaBuilder<T> Case<TValue>(
        string name,
        Func<T, bool> matches,
        Func<T, TValue> get,
        Func<TValue, T> create,
        Schema<TValue> target,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cases.Add(
            new UnionCaseSchema<T, TValue>(
                id.WithMember(name),
                target,
                matches,
                get,
                create,
                traits
            )
        );
        return this;
    }

    public UnionSchema<T> Build()
    {
        if (cases.Count == 0)
        {
            throw new InvalidOperationException($"Union schema '{id}' requires at least one case.");
        }

        return new UnionSchema<T>(id, cases, traits);
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

    public static StructSchemaBuilder<T, TBuilder> Structure<T, TBuilder>(
        ShapeId id,
        IEnumerable<Trait>? traits = null
    ) => new(id, traits);

    public static UnionSchemaBuilder<T> Union<T>(ShapeId id, IEnumerable<Trait>? traits = null) =>
        new(id, traits);

    /// <summary>A list read as <see cref="IReadOnlyList{T}"/>; the form a hand-written schema wants.</summary>
    public static CollectionSchema<
        IReadOnlyList<TElement>,
        TElement,
        List<TElement>
    > List<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new(
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

    public static CollectionSchema<TCollection, TElement, TBuilder> List<
        TCollection,
        TElement,
        TBuilder
    >(
        ShapeId id,
        Schema<TElement> element,
        Func<TCollection, IEnumerable<TElement>> getElements,
        Func<TBuilder> createBuilder,
        Action<TBuilder, TElement> add,
        Func<TBuilder, TCollection> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new(
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

    public static CollectionSchema<
        IReadOnlySet<TElement>,
        TElement,
        HashSet<TElement>
    > Set<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new(
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

    public static CollectionSchema<TCollection, TElement, TBuilder> Set<
        TCollection,
        TElement,
        TBuilder
    >(
        ShapeId id,
        Schema<TElement> element,
        Func<TCollection, IEnumerable<TElement>> getElements,
        Func<TBuilder> createBuilder,
        Action<TBuilder, TElement> add,
        Func<TBuilder, TCollection> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) =>
        new(
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
        new(
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
    ) => new(id, value, getEntries, createBuilder, add, build, traits, keyTraits, valueTraits, key);

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

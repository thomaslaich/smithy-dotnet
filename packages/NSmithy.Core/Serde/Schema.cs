using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;

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
        this.traits = BuildTraits(traits);
    }

    protected Schema()
    {
        traits = new Dictionary<ShapeId, Trait>();
    }

    public virtual ShapeId Id => id;

    public virtual ShapeKind Kind => kind;

    public virtual IReadOnlyDictionary<ShapeId, Trait> Traits => traits;

    public virtual Schema Resolved => this;

    public abstract TResult Accept<TResult>(ISchemaVisitor<TResult> visitor);

    public bool IsMember => Id.IsMember;

    public string? MemberName => Id.MemberName;

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);

    internal static IReadOnlyDictionary<ShapeId, Trait> BuildTraits(IEnumerable<Trait>? traits) =>
        traits is null ? [] : traits.ToDictionary(t => t.Id);
}

public abstract class Schema<T> : Schema
{
    protected Schema()
        : base() { }

    protected Schema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
        : base(id, kind, traits) { }
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
}

public sealed class ByteSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<sbyte>(id, ShapeKind.Byte, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitByte(this);
    }
}

public sealed class ShortSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<short>(id, ShapeKind.Short, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitShort(this);
    }
}

public sealed class IntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<int>(id, ShapeKind.Integer, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitInteger(this);
    }
}

public sealed class LongSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<long>(id, ShapeKind.Long, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitLong(this);
    }
}

public sealed class FloatSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<float>(id, ShapeKind.Float, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitFloat(this);
    }
}

public sealed class DoubleSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<double>(id, ShapeKind.Double, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitDouble(this);
    }
}

public sealed class BigIntegerSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<BigInteger>(id, ShapeKind.BigInteger, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBigInteger(this);
    }
}

public sealed class BigDecimalSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<decimal>(id, ShapeKind.BigDecimal, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBigDecimal(this);
    }
}

public sealed class StringSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<string>(id, ShapeKind.String, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitString(this);
    }
}

public sealed class BlobSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<byte[]>(id, ShapeKind.Blob, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitBlob(this);
    }
}

public sealed class StreamingBlobSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<Stream>(id, ShapeKind.Blob, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        throw new NotSupportedException("Streaming blob schemas are protocol-bound.");
    }
}

public sealed class TimestampSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<DateTimeOffset>(id, ShapeKind.Timestamp, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitTimestamp(this);
    }
}

public sealed class DocumentSchema(ShapeId id, IEnumerable<Trait>? traits = null)
    : PrimitiveSchema<Document>(id, ShapeKind.Document, traits)
{
    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitDocument(this);
    }
}

public sealed class UnitSchema : Schema<SmithyUnit>, IStructSchema<SmithyUnit, SmithyUnit>
{
    private static readonly IReadOnlyList<IMemberSchema> EmptyMembers = [];

    private static readonly IReadOnlyList<IMemberSchema<SmithyUnit>> EmptyTypedMembers = [];

    internal UnitSchema()
        : base(new ShapeId("smithy.api", "Unit"), ShapeKind.Structure) { }

    public IReadOnlyList<IMemberSchema> Members => EmptyMembers;

    public IReadOnlyList<IMemberSchema<SmithyUnit>> TypedMembers => EmptyTypedMembers;

    public IMemberSchema? GetMember(string name) => null;

    public void VisitMembers(IMemberVisitor<SmithyUnit> visitor) { }

    public void VisitMembers(IMemberVisitor<SmithyUnit, SmithyUnit> visitor) { }

    public SmithyUnit CreateTypedBuilder() => SmithyUnit.Value;

    public object CreateBuilder() => SmithyUnit.Value;

    public SmithyUnit Build(SmithyUnit builder) => SmithyUnit.Value;

    public object BuildObject(object builder) => SmithyUnit.Value;

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
        TargetSchema = target;
    }

    public Schema<T> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitNullable(this);
    }
}

public interface IStringEnumSchema
{
    object CreateObject(string value);
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
    internal StringEnumSchema(ShapeId id, IEnumerable<Trait>? traits = null)
        : base(id, ShapeKind.Enum, traits) { }

    public T Create(string value) => T.FromValue(value);

    public object CreateObject(string value) => T.FromValue(value)!;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStringEnum(this);
    }
}

public interface IIntEnumSchema
{
    int GetIntegerValueObject(object value);

    object CreateObject(int value);
}

public sealed class IntEnumSchema<T> : Schema<T>, IIntEnumSchema
    where T : struct, Enum
{
    internal IntEnumSchema(ShapeId id, IEnumerable<Trait>? traits = null)
        : base(id, ShapeKind.IntEnum, traits) { }

    public int GetIntegerValue(T value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

    public T Create(int value) => (T)Enum.ToObject(typeof(T), value);

    public int GetIntegerValueObject(object value) => GetIntegerValue((T)value);

    public object CreateObject(int value) => Create(value);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitIntEnum(this);
    }
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
}

public interface IStructSchema
{
    IReadOnlyList<IMemberSchema> Members { get; }

    IMemberSchema? GetMember(string name);

    object CreateBuilder();

    object BuildObject(object builder);
}

public interface IStructSchema<T> : IStructSchema
{
    IReadOnlyList<IMemberSchema<T>> TypedMembers { get; }

    void VisitMembers(IMemberVisitor<T> visitor);
}

public interface IStructSchema<T, TBuilder> : IStructSchema<T>
{
    TBuilder CreateTypedBuilder();

    T Build(TBuilder builder);

    void VisitMembers(IMemberVisitor<T, TBuilder> visitor);
}

public interface IMemberVisitor<TContainer>
{
    void Visit<TValue>(IMemberSchema<TContainer, TValue> member);
}

public interface IMemberVisitor<TContainer, TBuilder>
{
    void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member);
}

public interface IListSchema
{
    IMemberSchema ElementMember { get; }

    Schema Element { get; }

    IEnumerable<object?> GetElementsObject(object value);

    object CreateBuilder();

    void AddObject(object builder, object? value);

    object BuildObject(object builder);
}

public interface IListSchema<TCollection, TElement> : IListSchema
{
    ICollectionMemberSchema<TElement> TypedElementMember { get; }

    Schema<TElement> ElementSchema { get; }

    IEnumerable<TElement> GetElements(TCollection value);
}

public interface IListSchema<TCollection, TElement, TBuilder> : IListSchema<TCollection, TElement>
{
    TBuilder CreateTypedBuilder();

    void Add(TBuilder builder, TElement value);

    TCollection Build(TBuilder builder);
}

public interface IMapSchema
{
    IMemberSchema KeyMember { get; }

    IMemberSchema ValueMember { get; }

    Schema Value { get; }

    IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value);

    object CreateBuilder();

    void AddObject(object builder, string key, object? value);

    object BuildObject(object builder);
}

public interface IMapSchema<TDictionary, TValue> : IMapSchema
{
    ICollectionMemberSchema<string> TypedKeyMember { get; }

    ICollectionMemberSchema<TValue> TypedValueMember { get; }

    Schema<TValue> ValueSchema { get; }

    IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value);
}

public interface IMapSchema<TDictionary, TValue, TBuilder> : IMapSchema<TDictionary, TValue>
{
    TBuilder CreateTypedBuilder();

    void Add(TBuilder builder, string key, TValue value);

    TDictionary Build(TBuilder builder);
}

public interface IUnionSchema
{
    IReadOnlyList<IUnionCaseSchema> Cases { get; }

    IUnionCaseSchema? GetCase(string name);

    IUnionCaseSchema GetCaseObject(object value);
}

public interface IUnionSchema<T> : IUnionSchema
{
    void VisitCases(IUnionCaseVisitor<T> visitor);
}

public interface IUnionCaseVisitor<TUnion>
{
    void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase);
}

public interface IUnionCaseSchema
{
    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    Schema Target { get; }

    bool MatchesObject(object value);

    object? GetObject(object value);

    object CreateObject(object? value);
}

public interface IUnionCaseSchema<TUnion, TValue> : IUnionCaseSchema
{
    Schema<TValue> TargetSchema { get; }

    bool Matches(TUnion value);

    TValue GetValue(TUnion value);

    TUnion Create(TValue value);
}

public interface IMemberSchema
{
    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> MemberTraits { get; }

    Schema Target { get; }

    bool IsRequired { get; }

    Trait? GetTrait(ShapeId id);

    bool HasTrait(ShapeId id);

    object? GetObject(object container);

    void SetObject(object builder, object? value);
}

public interface IMemberSchema<TContainer> : IMemberSchema
{
    void Accept(IMemberVisitor<TContainer> visitor);
}

public interface IMemberSchema<TContainer, TValue> : IMemberSchema<TContainer>
{
    Schema<TValue> TargetSchema { get; }

    TValue GetValue(TContainer container);
}

public interface IMemberSchema<TContainer, TBuilder, TValue> : IMemberSchema<TContainer, TValue>
{
    void SetValue(TBuilder builder, TValue value);

    void Accept(IMemberVisitor<TContainer, TBuilder> visitor);
}

internal interface IBuilderMemberSchema<TContainer, TBuilder>
{
    void Accept(IMemberVisitor<TContainer, TBuilder> visitor);
}

public interface ICollectionMemberSchema<TValue> : IMemberSchema
{
    Schema<TValue> TargetSchema { get; }
}

public sealed class CollectionMemberSchema<TValue> : ICollectionMemberSchema<TValue>
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
        TargetSchema = target;
        memberTraits = Schema.BuildTraits(traits);
    }

    public ShapeId Id { get; }

    public string Name => Id.MemberName!;

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits => memberTraits;

    public Schema<TValue> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public bool IsRequired => true;

    public Trait? GetTrait(ShapeId id) =>
        MemberTraits.TryGetValue(id, out var trait) ? trait : TargetSchema.GetTrait(id);

    public bool HasTrait(ShapeId id) => GetTrait(id) is not null;

    public object? GetObject(object container) =>
        throw new NotSupportedException("Collection members do not expose container accessors.");

    public void SetObject(object builder, object? value) =>
        throw new NotSupportedException("Collection members do not expose builder setters.");
}

public sealed class ListSchema<TElement>
    : Schema<IReadOnlyList<TElement>>,
        IListSchema<IReadOnlyList<TElement>, TElement, List<TElement>>
{
    internal ListSchema(
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
        TypedElementMember = new CollectionMemberSchema<TElement>(
            id.WithMember("member"),
            element,
            elementTraits
        );
    }

    public ICollectionMemberSchema<TElement> TypedElementMember { get; }

    public IMemberSchema ElementMember => TypedElementMember;

    public Schema<TElement> ElementSchema { get; }

    public Schema Element => ElementSchema;

    public IEnumerable<TElement> GetElements(IReadOnlyList<TElement> value) => value;

    public IEnumerable<object?> GetElementsObject(object value)
    {
        foreach (var element in (IReadOnlyList<TElement>)value)
        {
            yield return element;
        }
    }

    public object CreateBuilder() => new List<TElement>();

    public List<TElement> CreateTypedBuilder() => new();

    public void Add(List<TElement> builder, TElement value) => builder.Add(value);

    public void AddObject(object builder, object? value) =>
        Add((List<TElement>)builder, (TElement)value!);

    public IReadOnlyList<TElement> Build(List<TElement> builder) =>
        new ReadOnlyCollection<TElement>(builder.ToArray());

    public object BuildObject(object builder) => Build((List<TElement>)builder);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitList(this);
    }
}

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

    public ICollectionMemberSchema<TElement> TypedElementMember { get; }

    public IMemberSchema ElementMember => TypedElementMember;

    public Schema<TElement> ElementSchema { get; }

    public Schema Element => ElementSchema;

    public IEnumerable<TElement> GetElements(TCollection value) => getElements(value);

    public IEnumerable<object?> GetElementsObject(object value)
    {
        foreach (var element in getElements((TCollection)value))
        {
            yield return element;
        }
    }

    public object CreateBuilder() => createBuilder()!;

    public TBuilder CreateTypedBuilder() => createBuilder();

    public void Add(TBuilder builder, TElement value) => add(builder, value);

    public void AddObject(object builder, object? value) =>
        Add((TBuilder)builder, (TElement)value!);

    public TCollection Build(TBuilder builder) => build(builder);

    public object BuildObject(object builder) => Build((TBuilder)builder)!;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitList(this);
    }
}

public sealed class SetSchema<TElement>
    : Schema<IReadOnlySet<TElement>>,
        IListSchema<IReadOnlySet<TElement>, TElement, HashSet<TElement>>
{
    internal SetSchema(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    )
        : base(id, ShapeKind.Set, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
        TypedElementMember = new CollectionMemberSchema<TElement>(
            id.WithMember("member"),
            element,
            elementTraits
        );
    }

    public ICollectionMemberSchema<TElement> TypedElementMember { get; }

    public IMemberSchema ElementMember => TypedElementMember;

    public Schema<TElement> ElementSchema { get; }

    public Schema Element => ElementSchema;

    public IEnumerable<TElement> GetElements(IReadOnlySet<TElement> value) => value;

    public IEnumerable<object?> GetElementsObject(object value)
    {
        foreach (var element in (IReadOnlySet<TElement>)value)
        {
            yield return element;
        }
    }

    public object CreateBuilder() => new HashSet<TElement>();

    public HashSet<TElement> CreateTypedBuilder() => new();

    public void Add(HashSet<TElement> builder, TElement value) => builder.Add(value);

    public void AddObject(object builder, object? value) =>
        Add((HashSet<TElement>)builder, (TElement)value!);

    public IReadOnlySet<TElement> Build(HashSet<TElement> builder) => builder;

    public object BuildObject(object builder) => Build((HashSet<TElement>)builder);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitList(this);
    }
}

public sealed class MapSchema<TValue>
    : Schema<IReadOnlyDictionary<string, TValue>>,
        IMapSchema<IReadOnlyDictionary<string, TValue>, TValue, Dictionary<string, TValue>>
{
    internal MapSchema(
        ShapeId id,
        Schema<TValue> value,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        TypedKeyMember = new CollectionMemberSchema<string>(
            id.WithMember("key"),
            Schemas.String,
            keyTraits
        );
        TypedValueMember = new CollectionMemberSchema<TValue>(
            id.WithMember("value"),
            value,
            valueTraits
        );
        ValueSchema = value;
    }

    public ICollectionMemberSchema<string> TypedKeyMember { get; }

    public IMemberSchema KeyMember => TypedKeyMember;

    public ICollectionMemberSchema<TValue> TypedValueMember { get; }

    public IMemberSchema ValueMember => TypedValueMember;

    public Schema<TValue> ValueSchema { get; }

    public Schema Value => ValueSchema;

    public IEnumerable<KeyValuePair<string, TValue>> GetEntries(
        IReadOnlyDictionary<string, TValue> value
    ) => value;

    public IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value)
    {
        foreach (var entry in (IReadOnlyDictionary<string, TValue>)value)
        {
            yield return new KeyValuePair<string, object?>(entry.Key, entry.Value);
        }
    }

    public object CreateBuilder() => new Dictionary<string, TValue>(StringComparer.Ordinal);

    public Dictionary<string, TValue> CreateTypedBuilder() => new(StringComparer.Ordinal);

    public void Add(Dictionary<string, TValue> builder, string key, TValue value) =>
        builder.Add(key, value);

    public void AddObject(object builder, string key, object? value) =>
        Add((Dictionary<string, TValue>)builder, key, (TValue)value!);

    public IReadOnlyDictionary<string, TValue> Build(Dictionary<string, TValue> builder) =>
        new ReadOnlyDictionary<string, TValue>(builder);

    public object BuildObject(object builder) => Build((Dictionary<string, TValue>)builder);

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitMap(this);
    }
}

public sealed class DictionarySchema<TDictionary, TValue, TBuilder>
    : Schema<TDictionary>,
        IMapSchema<TDictionary, TValue, TBuilder>
{
    private readonly Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries;
    private readonly Func<TBuilder> createBuilder;
    private readonly Action<TBuilder, string, TValue> add;
    private readonly Func<TBuilder, TDictionary> build;

    internal DictionarySchema(
        ShapeId id,
        Schema<TValue> value,
        Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries,
        Func<TBuilder> createBuilder,
        Action<TBuilder, string, TValue> add,
        Func<TBuilder, TDictionary> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(getEntries);
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(build);

        TypedKeyMember = new CollectionMemberSchema<string>(
            id.WithMember("key"),
            Schemas.String,
            keyTraits
        );
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

    public ICollectionMemberSchema<string> TypedKeyMember { get; }

    public IMemberSchema KeyMember => TypedKeyMember;

    public ICollectionMemberSchema<TValue> TypedValueMember { get; }

    public IMemberSchema ValueMember => TypedValueMember;

    public Schema<TValue> ValueSchema { get; }

    public Schema Value => ValueSchema;

    public IEnumerable<KeyValuePair<string, TValue>> GetEntries(TDictionary value) =>
        getEntries(value);

    public IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value)
    {
        foreach (var entry in getEntries((TDictionary)value))
        {
            yield return new KeyValuePair<string, object?>(entry.Key, entry.Value);
        }
    }

    public object CreateBuilder() => createBuilder()!;

    public TBuilder CreateTypedBuilder() => createBuilder();

    public void Add(TBuilder builder, string key, TValue value) => add(builder, key, value);

    public void AddObject(object builder, string key, object? value) =>
        Add((TBuilder)builder, key, (TValue)value!);

    public TDictionary Build(TBuilder builder) => build(builder);

    public object BuildObject(object builder) => Build((TBuilder)builder)!;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitMap(this);
    }
}

public sealed class UnionCaseSchema<TUnion, TValue>
    : IUnionCaseSchema<TUnion, TValue>,
        IUnionCaseSchema<TUnion>
{
    private readonly Func<TUnion, bool> matches;
    private readonly Func<TUnion, TValue> get;
    private readonly Func<TValue, TUnion> create;

    internal UnionCaseSchema(
        string name,
        Schema<TValue> target,
        Func<TUnion, bool> matches,
        Func<TUnion, TValue> get,
        Func<TValue, TUnion> create,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(create);

        Name = name;
        Traits = Schema.BuildTraits(traits);
        TargetSchema = target;
        this.matches = matches;
        this.get = get;
        this.create = create;
    }

    public string Name { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Schema<TValue> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public bool Matches(TUnion value) => matches(value);

    public TValue GetValue(TUnion value) => get(value);

    public TUnion Create(TValue value) => create(value);

    public bool MatchesObject(object value) => matches((TUnion)value);

    public object? GetObject(object value) => get((TUnion)value);

    public object CreateObject(object? value) => create((TValue)value!)!;

    public void Accept(IUnionCaseVisitor<TUnion> visitor) => visitor.Visit(this);
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

    public IUnionCaseSchema GetCaseObject(object value)
    {
        foreach (var @case in cases)
        {
            if (@case.MatchesObject(value))
            {
                return @case;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }

    public void VisitCases(IUnionCaseVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var @case in cases)
        {
            ((IUnionCaseSchema<T>)@case).Accept(visitor);
        }
    }

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

internal interface IUnionCaseSchema<TUnion>
{
    void Accept(IUnionCaseVisitor<TUnion> visitor);
}

public sealed class MemberSchema<TContainer, TBuilder, TValue>
    : Schema<TValue>,
        IMemberSchema<TContainer, TBuilder, TValue>,
        IBuilderMemberSchema<TContainer, TBuilder>
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
        TargetSchema = target;
        this.get = get;
        this.set = set;
    }

    public string Name => MemberName!;

    public bool IsRequired { get; }

    public IReadOnlyDictionary<ShapeId, Trait> MemberTraits => base.Traits;

    public Schema<TValue> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public TValue GetValue(TContainer container) => get(container);

    public void Set(TBuilder builder, TValue value) => set(builder, value);

    public void SetValue(TBuilder builder, TValue value) => set(builder, value);

    public void Accept(IMemberVisitor<TContainer> visitor) => visitor.Visit(this);

    public void Accept(IMemberVisitor<TContainer, TBuilder> visitor) => visitor.Visit(this);

    public object? GetObject(object container) => get((TContainer)container);

    public void SetObject(object builder, object? value) => set((TBuilder)builder, (TValue)value!);

    public new Trait? GetTrait(ShapeId id) =>
        MemberTraits.TryGetValue(id, out var trait) ? trait : TargetSchema.GetTrait(id);

    public new bool HasTrait(ShapeId id) => GetTrait(id) is not null;

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return TargetSchema.Accept(visitor);
    }
}

public sealed class StructSchema<T, TBuilder> : Schema<T>, IStructSchema<T, TBuilder>
{
    private readonly Func<TBuilder> createBuilder;
    private readonly Func<TBuilder, T> build;
    private readonly ReadOnlyCollection<IMemberSchema> members;
    private readonly ReadOnlyCollection<IMemberSchema<T>> typedMembers;
    private readonly Dictionary<string, IMemberSchema> membersByName;

    internal StructSchema(
        ShapeId id,
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IReadOnlyList<IMemberSchema> members,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Structure, traits)
    {
        this.createBuilder = createBuilder;
        this.build = build;
        this.members = new ReadOnlyCollection<IMemberSchema>(members.ToArray());
        typedMembers = new ReadOnlyCollection<IMemberSchema<T>>(
            this.members.Cast<IMemberSchema<T>>().ToArray()
        );
        membersByName = BuildMembersByName(this.members);
    }

    public IReadOnlyList<IMemberSchema> Members => members;

    public IReadOnlyList<IMemberSchema<T>> TypedMembers => typedMembers;

    public IMemberSchema? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public TBuilder CreateTypedBuilder() => createBuilder();

    public T Build(TBuilder builder) => build(builder);

    public object CreateBuilder() => createBuilder()!;

    public object BuildObject(object builder) => build((TBuilder)builder)!;

    public void VisitMembers(IMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in typedMembers)
        {
            member.Accept(visitor);
        }
    }

    public void VisitMembers(IMemberVisitor<T, TBuilder> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in members)
        {
            ((IBuilderMemberSchema<T, TBuilder>)member).Accept(visitor);
        }
    }

    public override TResult Accept<TResult>(ISchemaVisitor<TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitStruct(this);
    }

    private static Dictionary<string, IMemberSchema> BuildMembersByName(
        ReadOnlyCollection<IMemberSchema> members
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

public sealed class StructProjection<T, TBuilder>
{
    private readonly IStructSchema<T, TBuilder> source;
    private readonly ReadOnlyCollection<IMemberSchema<T>> typedMembers;
    private readonly Dictionary<string, IMemberSchema<T>> membersByName;

    internal StructProjection(
        IStructSchema<T, TBuilder> source,
        IReadOnlyList<IMemberSchema<T>> members
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);
        this.source = source;
        typedMembers = new ReadOnlyCollection<IMemberSchema<T>>(members.ToArray());
        membersByName = BuildMembersByName(typedMembers);
    }

    public IStructSchema<T, TBuilder> Source => source;

    public IReadOnlyList<IMemberSchema<T>> TypedMembers => typedMembers;

    public IMemberSchema<T>? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public void VisitMembers(IMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in typedMembers)
        {
            member.Accept(visitor);
        }
    }

    public void VisitMembers(IMemberVisitor<T, TBuilder> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in typedMembers)
        {
            ((IBuilderMemberSchema<T, TBuilder>)member).Accept(visitor);
        }
    }

    private static Dictionary<string, IMemberSchema<T>> BuildMembersByName(
        ReadOnlyCollection<IMemberSchema<T>> members
    )
    {
        var byName = new Dictionary<string, IMemberSchema<T>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            byName.Add(member.Name, member);
        }

        return byName;
    }
}

public sealed class StructSchemaBuilder<T, TBuilder>
{
    private readonly ShapeId id;
    private readonly IReadOnlyList<Trait>? traits;
    private readonly List<IMemberSchema> members = [];

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

    public StructSchema<T, TBuilder> Build(Func<TBuilder> createBuilder, Func<TBuilder, T> build)
    {
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(build);

        return new StructSchema<T, TBuilder>(id, createBuilder, build, members, traits);
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
        cases.Add(new UnionCaseSchema<T, TValue>(name, target, matches, get, create, traits));
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

public sealed class OperationSchema<TInput, TOutput>
{
    internal OperationSchema(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<IOperationErrorSchema>? errors = null,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        Id = id;
        Input = input;
        Output = output;
        Errors = errors?.ToArray() ?? [];
        Traits = Schema.BuildTraits(traits);
    }

    public ShapeId Id { get; }

    public ShapeKind Kind => ShapeKind.Operation;

    public Schema<TInput> Input { get; }

    public Schema<TOutput> Output { get; }

    public IReadOnlyList<IOperationErrorSchema> Errors { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);
}

public interface IOperationErrorSchema
{
    ShapeId Id { get; }

    Schema UntypedSchema { get; }

    int HttpStatusCode { get; }
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
    {
        Id = id;
        Traits = Schema.BuildTraits(traits);
    }

    public ShapeId Id { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);
}

public static class Schemas
{
    private const string PreludeNamespace = "smithy.api";

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

    public static StringEnumSchema<T> StringEnum<T>(ShapeId id, IEnumerable<Trait>? traits = null)
        where T : IStringEnumValue<T> => new(id, traits);

    public static IntEnumSchema<T> IntEnum<T>(ShapeId id, IEnumerable<Trait>? traits = null)
        where T : struct, Enum => new(id, traits);

    public static StructSchemaBuilder<T, TBuilder> Structure<T, TBuilder>(
        ShapeId id,
        IEnumerable<Trait>? traits = null
    ) => new(id, traits);

    public static UnionSchemaBuilder<T> Union<T>(ShapeId id, IEnumerable<Trait>? traits = null) =>
        new(id, traits);

    public static ListSchema<TElement> List<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) => new(id, ShapeKind.List, element, traits, elementTraits);

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

    public static SetSchema<TElement> Set<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? elementTraits = null
    ) => new(id, element, traits, elementTraits);

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

    public static MapSchema<TValue> Map<TValue>(
        ShapeId id,
        Schema<TValue> value,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null
    ) => new(id, value, traits, keyTraits, valueTraits);

    public static DictionarySchema<TDictionary, TValue, TBuilder> Map<
        TDictionary,
        TValue,
        TBuilder
    >(
        ShapeId id,
        Schema<TValue> value,
        Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> getEntries,
        Func<TBuilder> createBuilder,
        Action<TBuilder, string, TValue> add,
        Func<TBuilder, TDictionary> build,
        IEnumerable<Trait>? traits = null,
        IEnumerable<Trait>? keyTraits = null,
        IEnumerable<Trait>? valueTraits = null
    ) => new(id, value, getEntries, createBuilder, add, build, traits, keyTraits, valueTraits);

    public static OperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<Trait>? traits = null
    ) => new(id, input, output, null, traits);

    public static OperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<IOperationErrorSchema>? errors,
        IEnumerable<Trait>? traits = null
    ) => new(id, input, output, errors, traits);

    public static OperationErrorSchema<TError> OperationError<TError>(
        ShapeId id,
        Schema<TError> schema,
        int httpStatusCode
    )
        where TError : Exception => new(id, schema, httpStatusCode);

    public static ServiceSchema Service(ShapeId id, IEnumerable<Trait>? traits = null) =>
        new(id, traits);

    public static StructProjection<T, TBuilder> Project<T, TBuilder>(
        IStructSchema<T, TBuilder> source,
        IReadOnlyList<IMemberSchema<T>> members
    ) => new(source, members);
}

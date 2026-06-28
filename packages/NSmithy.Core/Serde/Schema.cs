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

public sealed class PrimitiveSchema<T> : Schema<T>
{
    internal PrimitiveSchema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
        : base(id, kind, traits) { }
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
}

/// <summary>
/// Wraps a target schema, overlaying additional traits (the wrapped member's traits) while
/// delegating shape and value behavior to the target via <see cref="Schema.Resolved"/>.
///
/// List and map elements are modeled as plain target schemas, so a member-level trait such as
/// <c>@xmlName</c> (which names each item element in non-flattened restXml lists) or
/// <c>@timestampFormat</c> would otherwise be lost. Codecs read these traits off the element
/// schema (e.g. the XML codec's item-element name); the overlay makes them visible there without
/// mutating the shared target schema, which is reused wherever the shape is referenced.
/// </summary>
public sealed class TraitOverlaySchema<T> : Schema<T>
{
    internal TraitOverlaySchema(Schema<T> target, IEnumerable<Trait> overlay)
        : base(target.Id, target.Kind, MergeTraits(target.Traits.Values, overlay))
    {
        TargetSchema = target;
    }

    public Schema<T> TargetSchema { get; }

    public override Schema Resolved => TargetSchema.Resolved;

    private static Dictionary<ShapeId, Trait>.ValueCollection MergeTraits(
        IEnumerable<Trait> baseTraits,
        IEnumerable<Trait> overlay
    )
    {
        var merged = new Dictionary<ShapeId, Trait>();
        foreach (var trait in baseTraits)
        {
            merged[trait.Id] = trait;
        }

        // The member's traits take precedence over the target shape's own traits.
        foreach (var trait in overlay)
        {
            merged[trait.Id] = trait;
        }

        return merged.Values;
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
    Schema Element { get; }

    IEnumerable<object?> GetElementsObject(object value);

    object CreateBuilder();

    void AddObject(object builder, object? value);

    object BuildObject(object builder);
}

public interface IListSchema<TCollection, TElement> : IListSchema
{
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
    Schema Value { get; }

    IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value);

    object CreateBuilder();

    void AddObject(object builder, string key, object? value);

    object BuildObject(object builder);
}

public interface IMapSchema<TDictionary, TValue> : IMapSchema
{
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

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    Schema Target { get; }

    bool IsRequired { get; }

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

public sealed class ListSchema<TElement>
    : Schema<IReadOnlyList<TElement>>,
        IListSchema<IReadOnlyList<TElement>, TElement, List<TElement>>
{
    internal ListSchema(
        ShapeId id,
        ShapeKind kind,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null
    )
        : base(id, kind, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
    }

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
        IEnumerable<Trait>? traits = null
    )
        : base(id, kind, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(getElements);
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(build);

        ElementSchema = element;
        this.getElements = getElements;
        this.createBuilder = createBuilder;
        this.add = add;
        this.build = build;
    }

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
}

public sealed class SetSchema<TElement>
    : Schema<IReadOnlySet<TElement>>,
        IListSchema<IReadOnlySet<TElement>, TElement, HashSet<TElement>>
{
    internal SetSchema(ShapeId id, Schema<TElement> element, IEnumerable<Trait>? traits = null)
        : base(id, ShapeKind.Set, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
    }

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
}

public sealed class MapSchema<TValue>
    : Schema<IReadOnlyDictionary<string, TValue>>,
        IMapSchema<IReadOnlyDictionary<string, TValue>, TValue, Dictionary<string, TValue>>
{
    internal MapSchema(ShapeId id, Schema<TValue> value, IEnumerable<Trait>? traits = null)
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValueSchema = value;
    }

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
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(getEntries);
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(add);
        ArgumentNullException.ThrowIfNull(build);

        ValueSchema = value;
        this.getEntries = getEntries;
        this.createBuilder = createBuilder;
        this.add = add;
        this.build = build;
    }

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

    public Schema<TValue> TargetSchema { get; }

    public Schema Target => TargetSchema;

    public TValue GetValue(TContainer container) => get(container);

    public void Set(TBuilder builder, TValue value) => set(builder, value);

    public void SetValue(TBuilder builder, TValue value) => set(builder, value);

    public void Accept(IMemberVisitor<TContainer> visitor) => visitor.Visit(this);

    public void Accept(IMemberVisitor<TContainer, TBuilder> visitor) => visitor.Visit(this);

    public object? GetObject(object container) => get((TContainer)container);

    public void SetObject(object builder, object? value) => set((TBuilder)builder, (TValue)value!);
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

public sealed class StructProjection<T>
{
    private readonly IStructSchema<T> source;
    private readonly ReadOnlyCollection<IMemberSchema<T>> typedMembers;
    private readonly Dictionary<string, IMemberSchema<T>> membersByName;

    internal StructProjection(IStructSchema<T> source, IReadOnlyList<IMemberSchema<T>> members)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);
        this.source = source;
        typedMembers = new ReadOnlyCollection<IMemberSchema<T>>(members.ToArray());
        membersByName = BuildMembersByName(typedMembers);
    }

    public IStructSchema<T> Source => source;

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
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        Id = id;
        Input = input;
        Output = output;
        Traits = Schema.BuildTraits(traits);
    }

    public ShapeId Id { get; }

    public ShapeKind Kind => ShapeKind.Operation;

    public Schema<TInput> Input { get; }

    public Schema<TOutput> Output { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);
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
        new PrimitiveSchema<bool>(new ShapeId(PreludeNamespace, "Boolean"), ShapeKind.Boolean);

    public static Schema<sbyte> Byte { get; } =
        new PrimitiveSchema<sbyte>(new ShapeId(PreludeNamespace, "Byte"), ShapeKind.Byte);

    public static Schema<short> Short { get; } =
        new PrimitiveSchema<short>(new ShapeId(PreludeNamespace, "Short"), ShapeKind.Short);

    public static Schema<int> Integer { get; } =
        new PrimitiveSchema<int>(new ShapeId(PreludeNamespace, "Integer"), ShapeKind.Integer);

    public static Schema<long> Long { get; } =
        new PrimitiveSchema<long>(new ShapeId(PreludeNamespace, "Long"), ShapeKind.Long);

    public static Schema<float> Float { get; } =
        new PrimitiveSchema<float>(new ShapeId(PreludeNamespace, "Float"), ShapeKind.Float);

    public static Schema<double> Double { get; } =
        new PrimitiveSchema<double>(new ShapeId(PreludeNamespace, "Double"), ShapeKind.Double);

    public static Schema<BigInteger> BigInteger { get; } =
        new PrimitiveSchema<BigInteger>(
            new ShapeId(PreludeNamespace, "BigInteger"),
            ShapeKind.BigInteger
        );

    public static Schema<decimal> BigDecimal { get; } =
        new PrimitiveSchema<decimal>(
            new ShapeId(PreludeNamespace, "BigDecimal"),
            ShapeKind.BigDecimal
        );

    public static Schema<string> String { get; } =
        new PrimitiveSchema<string>(new ShapeId(PreludeNamespace, "String"), ShapeKind.String);

    public static Schema<byte[]> Blob { get; } =
        new PrimitiveSchema<byte[]>(new ShapeId(PreludeNamespace, "Blob"), ShapeKind.Blob);

    public static Schema<Stream> StreamingBlob { get; } =
        new PrimitiveSchema<Stream>(
            new ShapeId(PreludeNamespace, "Blob"),
            ShapeKind.Blob,
            [new Trait(new ShapeId(PreludeNamespace, "streaming"))]
        );

    public static Schema<DateTimeOffset> Timestamp { get; } =
        new PrimitiveSchema<DateTimeOffset>(
            new ShapeId(PreludeNamespace, "Timestamp"),
            ShapeKind.Timestamp
        );

    /// <summary>
    /// A timestamp schema carrying traits (e.g. <c>@timestampFormat</c>) so codecs can resolve the
    /// wire format from the schema rather than a shared singleton.
    /// </summary>
    public static Schema<DateTimeOffset> TimestampWithTraits(IEnumerable<Trait>? traits) =>
        new PrimitiveSchema<DateTimeOffset>(
            new ShapeId(PreludeNamespace, "Timestamp"),
            ShapeKind.Timestamp,
            traits
        );

    public static Schema<Document> Document { get; } =
        new PrimitiveSchema<Document>(
            new ShapeId(PreludeNamespace, "Document"),
            ShapeKind.Document
        );

    public static Schema<SmithyUnit> Unit { get; } = new UnitSchema();

    public static Schema<T> Lazy<T>(Func<Schema<T>> resolve) => new LazySchema<T>(resolve);

    /// <summary>
    /// Overlays member-level traits onto an element/value schema (see
    /// <see cref="TraitOverlaySchema{T}"/>). Returns <paramref name="target"/> unchanged when there
    /// are no traits to add, so the common trait-free element keeps using the shared schema.
    /// </summary>
    public static Schema<T> WithTraits<T>(Schema<T> target, IEnumerable<Trait>? traits)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (traits is null)
        {
            return target;
        }

        var overlay = traits as IReadOnlyCollection<Trait> ?? traits.ToArray();
        return overlay.Count == 0 ? target : new TraitOverlaySchema<T>(target, overlay);
    }

    public static Schema<T?> Nullable<T>(Schema<T> target)
        where T : struct => new NullableSchema<T>(target);

    public static Schema<T?> NullableReference<T>(Schema<T> target)
        where T : class => (Schema<T?>)(object)target;

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
        IEnumerable<Trait>? traits = null
    ) => new(id, ShapeKind.List, element, traits);

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
        IEnumerable<Trait>? traits = null
    ) => new(id, ShapeKind.List, element, getElements, createBuilder, add, build, traits);

    public static SetSchema<TElement> Set<TElement>(
        ShapeId id,
        Schema<TElement> element,
        IEnumerable<Trait>? traits = null
    ) => new(id, element, traits);

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
        IEnumerable<Trait>? traits = null
    ) => new(id, ShapeKind.Set, element, getElements, createBuilder, add, build, traits);

    public static MapSchema<TValue> Map<TValue>(
        ShapeId id,
        Schema<TValue> value,
        IEnumerable<Trait>? traits = null
    ) => new(id, value, traits);

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
        IEnumerable<Trait>? traits = null
    ) => new(id, value, getEntries, createBuilder, add, build, traits);

    public static OperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        Schema<TInput> input,
        Schema<TOutput> output,
        IEnumerable<Trait>? traits = null
    ) => new(id, input, output, traits);

    public static ServiceSchema Service(ShapeId id, IEnumerable<Trait>? traits = null) =>
        new(id, traits);

    public static StructProjection<T> Project<T>(
        IStructSchema<T> source,
        IReadOnlyList<IMemberSchema<T>> members
    ) => new(source, members);
}

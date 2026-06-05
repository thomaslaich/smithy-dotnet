using System.Collections.ObjectModel;
using System.Numerics;

namespace NSmithy.Core.Functional;

public abstract class FunctionalSchema
{
    private readonly ShapeId id;
    private readonly ShapeKind kind;
    private readonly IReadOnlyDictionary<ShapeId, Trait> traits;

    protected FunctionalSchema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.kind = kind;
        this.traits = BuildTraits(traits);
    }

    protected FunctionalSchema()
    {
        traits = new Dictionary<ShapeId, Trait>();
    }

    public virtual ShapeId Id => id;

    public virtual ShapeKind Kind => kind;

    public virtual IReadOnlyDictionary<ShapeId, Trait> Traits => traits;

    public bool IsMember => Id.IsMember;

    public string? MemberName => Id.MemberName;

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);

    internal static IReadOnlyDictionary<ShapeId, Trait> BuildTraits(IEnumerable<Trait>? traits) =>
        traits is null ? new Dictionary<ShapeId, Trait>() : traits.ToDictionary(t => t.Id);
}

public abstract class FunctionalSchema<T> : FunctionalSchema
{
    protected FunctionalSchema()
        : base() { }

    protected FunctionalSchema(ShapeId id, ShapeKind kind, IEnumerable<Trait>? traits = null)
        : base(id, kind, traits) { }
}

public sealed class FunctionalPrimitiveSchema<T> : FunctionalSchema<T>
{
    internal FunctionalPrimitiveSchema(
        ShapeId id,
        ShapeKind kind,
        IEnumerable<Trait>? traits = null
    )
        : base(id, kind, traits) { }
}

public sealed class FunctionalLazySchema<T>
    : FunctionalSchema<T>,
        IFunctionalStructSchema,
        IFunctionalStructSchema<T>,
        IFunctionalListSchema,
        IFunctionalMapSchema,
        IFunctionalUnionSchema
{
    private readonly Lazy<FunctionalSchema<T>> target;

    internal FunctionalLazySchema(Func<FunctionalSchema<T>> resolve)
        : base()
    {
        ArgumentNullException.ThrowIfNull(resolve);
        target = new Lazy<FunctionalSchema<T>>(resolve);
    }

    public FunctionalSchema<T> TargetSchema => target.Value;

    public override ShapeId Id => TargetSchema.Id;

    public override ShapeKind Kind => TargetSchema.Kind;

    public override IReadOnlyDictionary<ShapeId, Trait> Traits => TargetSchema.Traits;

    public IReadOnlyList<IFunctionalMemberSchema> Members =>
        ((IFunctionalStructSchema)TargetSchema).Members;

    public IFunctionalMemberSchema? GetMember(string name) =>
        ((IFunctionalStructSchema)TargetSchema).GetMember(name);

    public IReadOnlyList<IFunctionalMemberSchema<T>> TypedMembers =>
        ((IFunctionalStructSchema<T>)TargetSchema).TypedMembers;

    public void VisitMembers(IFunctionalMemberVisitor<T> visitor) =>
        ((IFunctionalStructSchema<T>)TargetSchema).VisitMembers(visitor);

    public FunctionalSchema Element => ((IFunctionalListSchema)TargetSchema).Element;

    public FunctionalSchema Value => ((IFunctionalMapSchema)TargetSchema).Value;

    public IReadOnlyList<IFunctionalUnionCaseSchema> Cases =>
        ((IFunctionalUnionSchema)TargetSchema).Cases;

    public object CreateBuilder() => ((IFunctionalStructSchema)TargetSchema).CreateBuilder();

    public object BuildObject(object builder) =>
        ((IFunctionalStructSchema)TargetSchema).BuildObject(builder);

    public IEnumerable<object?> GetElementsObject(object value) =>
        ((IFunctionalListSchema)TargetSchema).GetElementsObject(value);

    public void AddObject(object builder, object? value) =>
        ((IFunctionalListSchema)TargetSchema).AddObject(builder, value);

    public IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value) =>
        ((IFunctionalMapSchema)TargetSchema).GetEntriesObject(value);

    public void AddObject(object builder, string key, object? value) =>
        ((IFunctionalMapSchema)TargetSchema).AddObject(builder, key, value);

    public IFunctionalUnionCaseSchema? GetCase(string name) =>
        ((IFunctionalUnionSchema)TargetSchema).GetCase(name);

    public IFunctionalUnionCaseSchema GetCaseObject(object value) =>
        ((IFunctionalUnionSchema)TargetSchema).GetCaseObject(value);
}

public interface IFunctionalStructSchema
{
    IReadOnlyList<IFunctionalMemberSchema> Members { get; }

    IFunctionalMemberSchema? GetMember(string name);

    object CreateBuilder();

    object BuildObject(object builder);
}

public interface IFunctionalStructSchema<T> : IFunctionalStructSchema
{
    IReadOnlyList<IFunctionalMemberSchema<T>> TypedMembers { get; }

    void VisitMembers(IFunctionalMemberVisitor<T> visitor);
}

public interface IFunctionalStructSchema<T, TBuilder> : IFunctionalStructSchema<T>
{
    TBuilder CreateTypedBuilder();

    T Build(TBuilder builder);
}

public interface IFunctionalMemberVisitor<TContainer>
{
    void Visit<TValue>(IFunctionalMemberSchema<TContainer, TValue> member);
}

public interface IFunctionalListSchema
{
    FunctionalSchema Element { get; }

    IEnumerable<object?> GetElementsObject(object value);

    object CreateBuilder();

    void AddObject(object builder, object? value);

    object BuildObject(object builder);
}

public interface IFunctionalMapSchema
{
    FunctionalSchema Value { get; }

    IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value);

    object CreateBuilder();

    void AddObject(object builder, string key, object? value);

    object BuildObject(object builder);
}

public interface IFunctionalUnionSchema
{
    IReadOnlyList<IFunctionalUnionCaseSchema> Cases { get; }

    IFunctionalUnionCaseSchema? GetCase(string name);

    IFunctionalUnionCaseSchema GetCaseObject(object value);
}

public interface IFunctionalUnionCaseSchema
{
    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    FunctionalSchema Target { get; }

    bool MatchesObject(object value);

    object? GetObject(object value);

    object CreateObject(object? value);
}

public interface IFunctionalMemberSchema
{
    string Name { get; }

    IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    FunctionalSchema Target { get; }

    bool IsRequired { get; }

    object? GetObject(object container);

    void SetObject(object builder, object? value);
}

public interface IFunctionalMemberSchema<TContainer> : IFunctionalMemberSchema
{
    void Accept(IFunctionalMemberVisitor<TContainer> visitor);
}

public interface IFunctionalMemberSchema<TContainer, TValue> : IFunctionalMemberSchema<TContainer>
{
    FunctionalSchema<TValue> TargetSchema { get; }

    TValue GetValue(TContainer container);
}

public sealed class FunctionalListSchema<TElement>
    : FunctionalSchema<IReadOnlyList<TElement>>,
        IFunctionalListSchema
{
    internal FunctionalListSchema(
        ShapeId id,
        ShapeKind kind,
        FunctionalSchema<TElement> element,
        IEnumerable<Trait>? traits = null
    )
        : base(id, kind, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
    }

    public FunctionalSchema<TElement> ElementSchema { get; }

    public FunctionalSchema Element => ElementSchema;

    public IEnumerable<object?> GetElementsObject(object value)
    {
        foreach (var element in (IReadOnlyList<TElement>)value)
        {
            yield return element;
        }
    }

    public object CreateBuilder() => new List<TElement>();

    public void AddObject(object builder, object? value) =>
        ((List<TElement>)builder).Add((TElement)value!);

    public object BuildObject(object builder) =>
        new ReadOnlyCollection<TElement>(((List<TElement>)builder).ToArray());
}

public sealed class FunctionalSetSchema<TElement>
    : FunctionalSchema<IReadOnlySet<TElement>>,
        IFunctionalListSchema
{
    internal FunctionalSetSchema(
        ShapeId id,
        FunctionalSchema<TElement> element,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Set, traits)
    {
        ArgumentNullException.ThrowIfNull(element);
        ElementSchema = element;
    }

    public FunctionalSchema<TElement> ElementSchema { get; }

    public FunctionalSchema Element => ElementSchema;

    public IEnumerable<object?> GetElementsObject(object value)
    {
        foreach (var element in (IReadOnlySet<TElement>)value)
        {
            yield return element;
        }
    }

    public object CreateBuilder() => new HashSet<TElement>();

    public void AddObject(object builder, object? value) =>
        ((HashSet<TElement>)builder).Add((TElement)value!);

    public object BuildObject(object builder) => (HashSet<TElement>)builder;
}

public sealed class FunctionalMapSchema<TValue>
    : FunctionalSchema<IReadOnlyDictionary<string, TValue>>,
        IFunctionalMapSchema
{
    internal FunctionalMapSchema(
        ShapeId id,
        FunctionalSchema<TValue> value,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Map, traits)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValueSchema = value;
    }

    public FunctionalSchema<TValue> ValueSchema { get; }

    public FunctionalSchema Value => ValueSchema;

    public IEnumerable<KeyValuePair<string, object?>> GetEntriesObject(object value)
    {
        foreach (var entry in (IReadOnlyDictionary<string, TValue>)value)
        {
            yield return new KeyValuePair<string, object?>(entry.Key, entry.Value);
        }
    }

    public object CreateBuilder() => new Dictionary<string, TValue>(StringComparer.Ordinal);

    public void AddObject(object builder, string key, object? value) =>
        ((Dictionary<string, TValue>)builder).Add(key, (TValue)value!);

    public object BuildObject(object builder) =>
        new ReadOnlyDictionary<string, TValue>((Dictionary<string, TValue>)builder);
}

public sealed class FunctionalUnionCaseSchema<TUnion, TValue> : IFunctionalUnionCaseSchema
{
    private readonly Func<TUnion, bool> matches;
    private readonly Func<TUnion, TValue> get;
    private readonly Func<TValue, TUnion> create;

    internal FunctionalUnionCaseSchema(
        string name,
        FunctionalSchema<TValue> target,
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
        Traits = FunctionalSchema.BuildTraits(traits);
        TargetSchema = target;
        this.matches = matches;
        this.get = get;
        this.create = create;
    }

    public string Name { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public FunctionalSchema<TValue> TargetSchema { get; }

    public FunctionalSchema Target => TargetSchema;

    public bool Matches(TUnion value) => matches(value);

    public TValue Get(TUnion value) => get(value);

    public TUnion Create(TValue value) => create(value);

    public bool MatchesObject(object value) => matches((TUnion)value);

    public object? GetObject(object value) => get((TUnion)value);

    public object CreateObject(object? value) => create((TValue)value!)!;
}

public sealed class FunctionalUnionSchema<T> : FunctionalSchema<T>, IFunctionalUnionSchema
{
    private readonly ReadOnlyCollection<IFunctionalUnionCaseSchema> cases;
    private readonly Dictionary<string, IFunctionalUnionCaseSchema> casesByName;

    internal FunctionalUnionSchema(
        ShapeId id,
        IReadOnlyList<IFunctionalUnionCaseSchema> cases,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Union, traits)
    {
        this.cases = new ReadOnlyCollection<IFunctionalUnionCaseSchema>(cases.ToArray());
        casesByName = BuildCasesByName(this.cases);
    }

    public IReadOnlyList<IFunctionalUnionCaseSchema> Cases => cases;

    public IFunctionalUnionCaseSchema? GetCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return casesByName.TryGetValue(name, out var @case) ? @case : null;
    }

    public IFunctionalUnionCaseSchema GetCaseObject(object value)
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

    private static Dictionary<string, IFunctionalUnionCaseSchema> BuildCasesByName(
        ReadOnlyCollection<IFunctionalUnionCaseSchema> cases
    )
    {
        var byName = new Dictionary<string, IFunctionalUnionCaseSchema>(StringComparer.Ordinal);
        foreach (var @case in cases)
        {
            byName.Add(@case.Name, @case);
        }

        return byName;
    }
}

public sealed class FunctionalMemberSchema<TContainer, TBuilder, TValue>
    : FunctionalSchema<TValue>,
        IFunctionalMemberSchema<TContainer, TValue>
{
    private readonly Func<TContainer, TValue> get;
    private readonly Action<TBuilder, TValue> set;

    internal FunctionalMemberSchema(
        ShapeId id,
        bool isRequired,
        FunctionalSchema<TValue> target,
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

    public FunctionalSchema<TValue> TargetSchema { get; }

    public FunctionalSchema Target => TargetSchema;

    public TValue GetValue(TContainer container) => get(container);

    public void Set(TBuilder builder, TValue value) => set(builder, value);

    public void Accept(IFunctionalMemberVisitor<TContainer> visitor) => visitor.Visit(this);

    public object? GetObject(object container) => get((TContainer)container);

    public void SetObject(object builder, object? value) => set((TBuilder)builder, (TValue)value!);
}

public sealed class FunctionalStructSchema<T, TBuilder>
    : FunctionalSchema<T>,
        IFunctionalStructSchema<T, TBuilder>
{
    private readonly Func<TBuilder> createBuilder;
    private readonly Func<TBuilder, T> build;
    private readonly ReadOnlyCollection<IFunctionalMemberSchema> members;
    private readonly ReadOnlyCollection<IFunctionalMemberSchema<T>> typedMembers;
    private readonly Dictionary<string, IFunctionalMemberSchema> membersByName;

    internal FunctionalStructSchema(
        ShapeId id,
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build,
        IReadOnlyList<IFunctionalMemberSchema> members,
        IEnumerable<Trait>? traits = null
    )
        : base(id, ShapeKind.Structure, traits)
    {
        this.createBuilder = createBuilder;
        this.build = build;
        this.members = new ReadOnlyCollection<IFunctionalMemberSchema>(members.ToArray());
        typedMembers = new ReadOnlyCollection<IFunctionalMemberSchema<T>>(
            this.members.Cast<IFunctionalMemberSchema<T>>().ToArray()
        );
        membersByName = BuildMembersByName(this.members);
    }

    public IReadOnlyList<IFunctionalMemberSchema> Members => members;

    public IReadOnlyList<IFunctionalMemberSchema<T>> TypedMembers => typedMembers;

    public IFunctionalMemberSchema? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public TBuilder CreateTypedBuilder() => createBuilder();

    public T Build(TBuilder builder) => build(builder);

    public object CreateBuilder() => createBuilder()!;

    public object BuildObject(object builder) => build((TBuilder)builder)!;

    public void VisitMembers(IFunctionalMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in typedMembers)
        {
            member.Accept(visitor);
        }
    }

    private static Dictionary<string, IFunctionalMemberSchema> BuildMembersByName(
        ReadOnlyCollection<IFunctionalMemberSchema> members
    )
    {
        var byName = new Dictionary<string, IFunctionalMemberSchema>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            byName.Add(member.Name, member);
        }

        return byName;
    }
}

public sealed class FunctionalStructProjection<T>
{
    private readonly IFunctionalStructSchema<T> source;
    private readonly ReadOnlyCollection<IFunctionalMemberSchema<T>> typedMembers;
    private readonly Dictionary<string, IFunctionalMemberSchema<T>> membersByName;

    internal FunctionalStructProjection(
        IFunctionalStructSchema<T> source,
        IReadOnlyList<IFunctionalMemberSchema<T>> members
    )
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(members);
        this.source = source;
        typedMembers = new ReadOnlyCollection<IFunctionalMemberSchema<T>>(members.ToArray());
        membersByName = BuildMembersByName(typedMembers);
    }

    public IFunctionalStructSchema<T> Source => source;

    public IReadOnlyList<IFunctionalMemberSchema<T>> TypedMembers => typedMembers;

    public IFunctionalMemberSchema<T>? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return membersByName.TryGetValue(name, out var member) ? member : null;
    }

    public void VisitMembers(IFunctionalMemberVisitor<T> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (var member in typedMembers)
        {
            member.Accept(visitor);
        }
    }

    private static Dictionary<string, IFunctionalMemberSchema<T>> BuildMembersByName(
        ReadOnlyCollection<IFunctionalMemberSchema<T>> members
    )
    {
        var byName = new Dictionary<string, IFunctionalMemberSchema<T>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            byName.Add(member.Name, member);
        }

        return byName;
    }
}

public sealed class FunctionalStructSchemaBuilder<T, TBuilder>
{
    private readonly ShapeId id;
    private readonly IReadOnlyList<Trait>? traits;
    private readonly List<IFunctionalMemberSchema> members = [];

    internal FunctionalStructSchemaBuilder(ShapeId id, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.traits = traits?.ToArray();
    }

    public FunctionalStructSchemaBuilder<T, TBuilder> Required<TValue>(
        string name,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        FunctionalSchema<TValue> target,
        IEnumerable<Trait>? traits = null
    ) => Member(name, isRequired: true, get, set, target, traits);

    public FunctionalStructSchemaBuilder<T, TBuilder> Optional<TValue>(
        string name,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        FunctionalSchema<TValue> target,
        IEnumerable<Trait>? traits = null
    ) => Member(name, isRequired: false, get, set, target, traits);

    public FunctionalStructSchema<T, TBuilder> Build(
        Func<TBuilder> createBuilder,
        Func<TBuilder, T> build
    )
    {
        ArgumentNullException.ThrowIfNull(createBuilder);
        ArgumentNullException.ThrowIfNull(build);

        return new FunctionalStructSchema<T, TBuilder>(id, createBuilder, build, members, traits);
    }

    private FunctionalStructSchemaBuilder<T, TBuilder> Member<TValue>(
        string name,
        bool isRequired,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set,
        FunctionalSchema<TValue> target,
        IEnumerable<Trait>? traits
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(target);

        members.Add(
            new FunctionalMemberSchema<T, TBuilder, TValue>(
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

public sealed class FunctionalUnionSchemaBuilder<T>
{
    private readonly ShapeId id;
    private readonly IReadOnlyList<Trait>? traits;
    private readonly List<IFunctionalUnionCaseSchema> cases = [];

    internal FunctionalUnionSchemaBuilder(ShapeId id, IEnumerable<Trait>? traits = null)
    {
        this.id = id;
        this.traits = traits?.ToArray();
    }

    public FunctionalUnionSchemaBuilder<T> Case<TValue>(
        string name,
        Func<T, bool> matches,
        Func<T, TValue> get,
        Func<TValue, T> create,
        FunctionalSchema<TValue> target,
        IEnumerable<Trait>? traits = null
    )
    {
        cases.Add(
            new FunctionalUnionCaseSchema<T, TValue>(name, target, matches, get, create, traits)
        );
        return this;
    }

    public FunctionalUnionSchema<T> Build()
    {
        if (cases.Count == 0)
        {
            throw new InvalidOperationException($"Union schema '{id}' requires at least one case.");
        }

        return new FunctionalUnionSchema<T>(id, cases, traits);
    }
}

public sealed class FunctionalOperationSchema<TInput, TOutput>
{
    internal FunctionalOperationSchema(
        ShapeId id,
        FunctionalSchema<TInput> input,
        FunctionalSchema<TOutput> output,
        IEnumerable<Trait>? traits = null
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        Id = id;
        Input = input;
        Output = output;
        Traits = FunctionalSchema.BuildTraits(traits);
    }

    public ShapeId Id { get; }

    public ShapeKind Kind => ShapeKind.Operation;

    public FunctionalSchema<TInput> Input { get; }

    public FunctionalSchema<TOutput> Output { get; }

    public IReadOnlyDictionary<ShapeId, Trait> Traits { get; }

    public Trait? GetTrait(ShapeId id) => Traits.TryGetValue(id, out var trait) ? trait : null;

    public bool HasTrait(ShapeId id) => Traits.ContainsKey(id);
}

public static class FunctionalSchemas
{
    private const string PreludeNamespace = "smithy.api";

    public static FunctionalSchema<bool> Boolean { get; } =
        new FunctionalPrimitiveSchema<bool>(
            new ShapeId(PreludeNamespace, "Boolean"),
            ShapeKind.Boolean
        );

    public static FunctionalSchema<sbyte> Byte { get; } =
        new FunctionalPrimitiveSchema<sbyte>(new ShapeId(PreludeNamespace, "Byte"), ShapeKind.Byte);

    public static FunctionalSchema<short> Short { get; } =
        new FunctionalPrimitiveSchema<short>(
            new ShapeId(PreludeNamespace, "Short"),
            ShapeKind.Short
        );

    public static FunctionalSchema<int> Integer { get; } =
        new FunctionalPrimitiveSchema<int>(
            new ShapeId(PreludeNamespace, "Integer"),
            ShapeKind.Integer
        );

    public static FunctionalSchema<long> Long { get; } =
        new FunctionalPrimitiveSchema<long>(new ShapeId(PreludeNamespace, "Long"), ShapeKind.Long);

    public static FunctionalSchema<float> Float { get; } =
        new FunctionalPrimitiveSchema<float>(
            new ShapeId(PreludeNamespace, "Float"),
            ShapeKind.Float
        );

    public static FunctionalSchema<double> Double { get; } =
        new FunctionalPrimitiveSchema<double>(
            new ShapeId(PreludeNamespace, "Double"),
            ShapeKind.Double
        );

    public static FunctionalSchema<BigInteger> BigInteger { get; } =
        new FunctionalPrimitiveSchema<BigInteger>(
            new ShapeId(PreludeNamespace, "BigInteger"),
            ShapeKind.BigInteger
        );

    public static FunctionalSchema<decimal> BigDecimal { get; } =
        new FunctionalPrimitiveSchema<decimal>(
            new ShapeId(PreludeNamespace, "BigDecimal"),
            ShapeKind.BigDecimal
        );

    public static FunctionalSchema<string> String { get; } =
        new FunctionalPrimitiveSchema<string>(
            new ShapeId(PreludeNamespace, "String"),
            ShapeKind.String
        );

    public static FunctionalSchema<byte[]> Blob { get; } =
        new FunctionalPrimitiveSchema<byte[]>(
            new ShapeId(PreludeNamespace, "Blob"),
            ShapeKind.Blob
        );

    public static FunctionalSchema<DateTimeOffset> Timestamp { get; } =
        new FunctionalPrimitiveSchema<DateTimeOffset>(
            new ShapeId(PreludeNamespace, "Timestamp"),
            ShapeKind.Timestamp
        );

    public static FunctionalSchema<Document> Document { get; } =
        new FunctionalPrimitiveSchema<Document>(
            new ShapeId(PreludeNamespace, "Document"),
            ShapeKind.Document
        );

    public static FunctionalSchema<T> Lazy<T>(Func<FunctionalSchema<T>> resolve) =>
        new FunctionalLazySchema<T>(resolve);

    public static FunctionalStructSchemaBuilder<T, TBuilder> Structure<T, TBuilder>(
        ShapeId id,
        IEnumerable<Trait>? traits = null
    ) => new(id, traits);

    public static FunctionalUnionSchemaBuilder<T> Union<T>(
        ShapeId id,
        IEnumerable<Trait>? traits = null
    ) => new(id, traits);

    public static FunctionalListSchema<TElement> List<TElement>(
        ShapeId id,
        FunctionalSchema<TElement> element,
        IEnumerable<Trait>? traits = null
    ) => new(id, ShapeKind.List, element, traits);

    public static FunctionalSetSchema<TElement> Set<TElement>(
        ShapeId id,
        FunctionalSchema<TElement> element,
        IEnumerable<Trait>? traits = null
    ) => new(id, element, traits);

    public static FunctionalMapSchema<TValue> Map<TValue>(
        ShapeId id,
        FunctionalSchema<TValue> value,
        IEnumerable<Trait>? traits = null
    ) => new(id, value, traits);

    public static FunctionalOperationSchema<TInput, TOutput> Operation<TInput, TOutput>(
        ShapeId id,
        FunctionalSchema<TInput> input,
        FunctionalSchema<TOutput> output,
        IEnumerable<Trait>? traits = null
    ) => new(id, input, output, traits);

    public static FunctionalStructProjection<T> Project<T>(
        IFunctionalStructSchema<T> source,
        IReadOnlyList<IFunctionalMemberSchema<T>> members
    ) => new(source, members);
}

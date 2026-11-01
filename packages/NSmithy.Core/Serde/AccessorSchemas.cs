namespace NSmithy.Core.Serde;

// Schemas that read and write a value through accessors instead of code of their own. They serve a
// language that cannot override the generic members of Schema<T> and its kin (F# cannot spell
// `allows ref struct`): its generated schemas derive from these bases, override only the non-generic
// members, and hand over one accessor per member.

/// <summary>
/// A structure member: the schema it is read and written with, and the functions that get it from a
/// value and set it on a builder. <see cref="StructMember"/> creates one.
/// </summary>
public abstract class StructMember<T, TBuilder>
{
    private protected StructMember(MemberSchema member)
    {
        ArgumentNullException.ThrowIfNull(member);
        Member = member;
    }

    public MemberSchema Member { get; }

    internal abstract void Serialize<TSerializer>(int index, T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    internal abstract void Deserialize<TDeserializer>(
        TBuilder builder,
        ref TDeserializer deserializer
    )
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public static class StructMember
{
    /// <summary>
    /// A member written and read with <paramref name="schema"/>. A null value read from the wire
    /// leaves the builder as it is, which is how a member is absent.
    /// </summary>
    public static StructMember<T, TBuilder> Create<T, TBuilder, TValue>(
        MemberSchema member,
        Schema<TValue> schema,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set
    ) => new AccessorStructMember<T, TBuilder, TValue>(member, schema, get, set);
}

internal sealed class AccessorStructMember<T, TBuilder, TValue> : StructMember<T, TBuilder>
{
    private readonly Schema<TValue> schema;
    private readonly Func<T, TValue> get;
    private readonly Action<TBuilder, TValue> set;

    public AccessorStructMember(
        MemberSchema member,
        Schema<TValue> schema,
        Func<T, TValue> get,
        Action<TBuilder, TValue> set
    )
        : base(member)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        this.schema = schema;
        this.get = get;
        this.set = set;
    }

    internal override void Serialize<TSerializer>(int index, T value, ref TSerializer serializer) =>
        schema.Write(index, get(value), ref serializer);

    internal override void Deserialize<TDeserializer>(
        TBuilder builder,
        ref TDeserializer deserializer
    )
    {
        // A value type cannot be read from a null; a reference type reads one as null.
        if (typeof(TValue).IsValueType && deserializer.TryReadNull())
        {
            return;
        }

        var value = schema.Read(ref deserializer);
        if (value is not null)
        {
            set(builder, value);
        }
    }
}

/// <summary>A structure whose members are read and written through <see cref="StructMember{T, TBuilder}"/>s.</summary>
public abstract class AccessorStructSchema<T, TBuilder>(
    ShapeId id,
    StructMember<T, TBuilder>[] members,
    IEnumerable<Trait>? traits = null
) : StructSchema<T, TBuilder>(id, MembersOf(members), traits)
{
    private readonly StructMember<T, TBuilder>[] accessors = members;

    public sealed override void SerializeMembers<TSerializer>(T value, ref TSerializer serializer)
    {
        for (var index = 0; index < accessors.Length; index++)
        {
            accessors[index].Serialize(index, value, ref serializer);
        }
    }

    public sealed override void DeserializeMember<TDeserializer>(
        TBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
    {
        if ((uint)index < (uint)accessors.Length)
        {
            accessors[index].Deserialize(builder, ref deserializer);
        }
    }

    private static IEnumerable<MemberSchema> MembersOf(StructMember<T, TBuilder>[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return members.Select(member => member.Member);
    }
}

/// <summary>
/// A union case: the schema the value it holds is read and written with, and the functions that get
/// that value from a union and make a union of it. <see cref="UnionCase"/> creates one.
/// </summary>
public abstract class UnionCase<T>
{
    private protected UnionCase(MemberSchema member)
    {
        ArgumentNullException.ThrowIfNull(member);
        Member = member;
    }

    public MemberSchema Member { get; }

    internal abstract void Serialize<TSerializer>(int index, T value, ref TSerializer serializer)
        where TSerializer : struct, IShapeSerializer, allows ref struct;

    internal abstract T Deserialize<TDeserializer>(ref TDeserializer deserializer)
        where TDeserializer : struct, IShapeDeserializer, allows ref struct;
}

public static class UnionCase
{
    public static UnionCase<T> Create<T, TValue>(
        MemberSchema member,
        Schema<TValue> schema,
        Func<T, TValue> get,
        Func<TValue, T> create
    ) => new AccessorUnionCase<T, TValue>(member, schema, get, create);
}

internal sealed class AccessorUnionCase<T, TValue> : UnionCase<T>
{
    private readonly Schema<TValue> schema;
    private readonly Func<T, TValue> get;
    private readonly Func<TValue, T> create;

    public AccessorUnionCase(
        MemberSchema member,
        Schema<TValue> schema,
        Func<T, TValue> get,
        Func<TValue, T> create
    )
        : base(member)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(create);
        this.schema = schema;
        this.get = get;
        this.create = create;
    }

    internal override void Serialize<TSerializer>(int index, T value, ref TSerializer serializer) =>
        schema.Write(index, get(value), ref serializer);

    internal override T Deserialize<TDeserializer>(ref TDeserializer deserializer) =>
        create(schema.Read(ref deserializer));
}

/// <summary>A union whose cases are read and written through <see cref="UnionCase{T}"/>s.</summary>
public abstract class AccessorUnionSchema<T>(
    ShapeId id,
    UnionCase<T>[] cases,
    IEnumerable<Trait>? traits = null
) : UnionSchema<T>(id, CasesOf(cases), traits)
{
    private readonly UnionCase<T>[] accessors = cases;

    public sealed override void SerializeCase<TSerializer>(T value, ref TSerializer serializer)
    {
        var index = CaseOf(value);
        if ((uint)index >= (uint)accessors.Length)
        {
            throw NoCaseMatched();
        }

        accessors[index].Serialize(index, value, ref serializer);
    }

    public sealed override T DeserializeCase<TDeserializer>(
        int index,
        ref TDeserializer deserializer
    ) =>
        (uint)index < (uint)accessors.Length
            ? accessors[index].Deserialize(ref deserializer)
            : throw NoCaseMatched();

    private static IEnumerable<MemberSchema> CasesOf(UnionCase<T>[] cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        return cases.Select(@case => @case.Member);
    }
}

/// <summary>
/// A value that may be absent, held in a type of its own such as F#'s <c>option</c>: absent writes
/// and reads as null, present goes through the target schema.
/// </summary>
public sealed class OptionalSchema<TOptional, T> : Schema<TOptional>, INullableSchema
{
    private readonly Func<TOptional, bool> isPresent;
    private readonly Func<TOptional, T> valueOf;
    private readonly Func<T, TOptional> present;
    private readonly TOptional absent;

    internal OptionalSchema(
        Schema<T> target,
        Func<TOptional, bool> isPresent,
        Func<TOptional, T> valueOf,
        Func<T, TOptional> present,
        TOptional absent
    )
        : base()
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(isPresent);
        ArgumentNullException.ThrowIfNull(valueOf);
        ArgumentNullException.ThrowIfNull(present);
        TypedTarget = target;
        this.isPresent = isPresent;
        this.valueOf = valueOf;
        this.present = present;
        this.absent = absent;
    }

    public Schema<T> TypedTarget { get; }

    public Schema Target => TypedTarget;

    // Read through the target on each access: it may be a lazy schema not yet resolvable.
    public override ShapeId Id => TypedTarget.Id;

    public override ShapeKind Kind => TypedTarget.Kind;

    public override IReadOnlyDictionary<ShapeId, Trait> Traits => TypedTarget.Traits;

    public override void Write<TSerializer>(int member, TOptional value, ref TSerializer serializer)
    {
        if (value is not null && isPresent(value))
        {
            TypedTarget.Write(member, valueOf(value), ref serializer);
        }
        else
        {
            serializer.WriteNull(member);
        }
    }

    public override TOptional Read<TDeserializer>(ref TDeserializer deserializer) =>
        deserializer.TryReadNull() ? absent : present(TypedTarget.Read(ref deserializer));
}

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using NSmithy.Core.Serde;

namespace NSmithy.Core.Validation;

/// <summary>
/// Value equality derived from a schema, for <c>@uniqueItems</c>. .NET equality is the wrong
/// authority here: a blob is a <see cref="byte"/>[] and a generated structure holding a list or map
/// compares that member by reference, so duplicates the model forbids would pass unnoticed. Walking
/// the schema instead compares what the model says the value is.
/// </summary>
internal static class SchemaEqualityComparer
{
    /// <summary>
    /// Compilation is deferred so a self-referencing element schema does not recurse forever while
    /// being compiled.
    /// </summary>
    public static IEqualityComparer<T> For<T>(Schema<T> schema) => new DeferredComparer<T>(schema);

    internal static IEqualityComparer<T> Compile<T>(Schema<T> schema) =>
        (IEqualityComparer<T>)schema.Resolved.Accept(ComparerCompiler.Instance);

    private sealed class ComparerCompiler : ISchemaVisitor<object>
    {
        public static ComparerCompiler Instance { get; } = new();

        public object VisitBoolean(Schema<bool> schema) => EqualityComparer<bool>.Default;

        public object VisitByte(Schema<sbyte> schema) => EqualityComparer<sbyte>.Default;

        public object VisitShort(Schema<short> schema) => EqualityComparer<short>.Default;

        public object VisitInteger(Schema<int> schema) => EqualityComparer<int>.Default;

        public object VisitLong(Schema<long> schema) => EqualityComparer<long>.Default;

        public object VisitFloat(Schema<float> schema) => EqualityComparer<float>.Default;

        public object VisitDouble(Schema<double> schema) => EqualityComparer<double>.Default;

        public object VisitBigInteger(Schema<BigInteger> schema) =>
            EqualityComparer<BigInteger>.Default;

        public object VisitBigDecimal(Schema<decimal> schema) => EqualityComparer<decimal>.Default;

        public object VisitString(Schema<string> schema) => StringComparer.Ordinal;

        public object VisitBlob(Schema<byte[]> schema) => BlobComparer.Instance;

        // Neither can appear in a list: @uniqueItems needs to hold every element at once, and these
        // are consumed once. Reference equality is the only answer available.
        public object VisitStreamingBlob(Schema<Stream> schema) => EqualityComparer<Stream>.Default;

        public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
            EqualityComparer<IAsyncEnumerable<TEvent>>.Default;

        public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
            EqualityComparer<DateTimeOffset>.Default;

        public object VisitDocument(Schema<Document> schema) => EqualityComparer<Document>.Default;

        public object VisitNullable<T>(NullableSchema<T> schema)
            where T : struct => new NullableComparer<T>(For(schema.TargetSchema));

        public object VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        ) => new ListComparer<TCollection, TElement>(schema);

        public object VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        ) => new MapComparer<TDictionary, TValue>(schema);

        public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
            new StructComparer<T>(schema);

        public object VisitUnion<T>(IUnionSchema<T> schema) => new UnionComparer<T>(schema);

        public object VisitStringEnum<T>(StringEnumSchema<T> schema)
            where T : IStringEnumValue<T> => EqualityComparer<T>.Default;

        public object VisitIntEnum<T>(IntEnumSchema<T> schema)
            where T : struct, Enum => EqualityComparer<T>.Default;
    }
}

internal sealed class DeferredComparer<T>(Schema<T> schema) : IEqualityComparer<T>
{
    private readonly Lazy<IEqualityComparer<T>> inner = new(() =>
        SchemaEqualityComparer.Compile(schema)
    );

    public bool Equals(T? x, T? y) => inner.Value.Equals(x, y);

    public int GetHashCode([DisallowNull] T obj) => inner.Value.GetHashCode(obj);
}

internal sealed class BlobComparer : IEqualityComparer<byte[]>
{
    public static BlobComparer Instance { get; } = new();

    public bool Equals(byte[]? x, byte[]? y) =>
        x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);

    public int GetHashCode([DisallowNull] byte[] obj)
    {
        var hash = new HashCode();
        hash.AddBytes(obj);
        return hash.ToHashCode();
    }
}

internal sealed class NullableComparer<T>(IEqualityComparer<T> inner) : IEqualityComparer<T?>
    where T : struct
{
    public bool Equals(T? x, T? y) =>
        x.HasValue ? y.HasValue && inner.Equals(x.Value, y.Value) : !y.HasValue;

    public int GetHashCode([DisallowNull] T? obj) =>
        obj.HasValue ? inner.GetHashCode(obj.Value) : 0;
}

internal sealed class ListComparer<TCollection, TElement> : IEqualityComparer<TCollection>
{
    private readonly IListSchema<TCollection, TElement> schema;
    private readonly IEqualityComparer<TElement> elementComparer;

    public ListComparer(IListSchema<TCollection, TElement> schema)
    {
        this.schema = schema;
        elementComparer = SchemaEqualityComparer.For(schema.ElementSchema);
    }

    public bool Equals(TCollection? x, TCollection? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        using var left = schema.GetElements(x).GetEnumerator();
        using var right = schema.GetElements(y).GetEnumerator();
        while (left.MoveNext())
        {
            if (!right.MoveNext() || !elementComparer.Equals(left.Current, right.Current))
            {
                return false;
            }
        }

        return !right.MoveNext();
    }

    public int GetHashCode([DisallowNull] TCollection obj)
    {
        var hash = new HashCode();
        foreach (var element in schema.GetElements(obj))
        {
            hash.Add(element is null ? 0 : elementComparer.GetHashCode(element));
        }

        return hash.ToHashCode();
    }
}

internal sealed class MapComparer<TDictionary, TValue> : IEqualityComparer<TDictionary>
{
    private readonly IMapSchema<TDictionary, TValue> schema;
    private readonly IEqualityComparer<TValue> valueComparer;

    public MapComparer(IMapSchema<TDictionary, TValue> schema)
    {
        this.schema = schema;
        valueComparer = SchemaEqualityComparer.For(schema.ValueSchema);
    }

    public bool Equals(TDictionary? x, TDictionary? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        var right = schema
            .GetEntries(y)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var count = 0;
        foreach (var entry in schema.GetEntries(x))
        {
            if (
                !right.TryGetValue(entry.Key, out var other)
                || !valueComparer.Equals(entry.Value, other)
            )
            {
                return false;
            }

            count++;
        }

        return count == right.Count;
    }

    public int GetHashCode([DisallowNull] TDictionary obj)
    {
        // Order-independent: a map's entry order is not part of its value.
        var hash = 0;
        foreach (var entry in schema.GetEntries(obj))
        {
            hash ^= HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(entry.Key),
                entry.Value is null ? 0 : valueComparer.GetHashCode(entry.Value)
            );
        }

        return hash;
    }
}

internal sealed class StructComparer<T> : IEqualityComparer<T>, IMemberVisitor<T>
{
    private readonly List<IMemberComparer<T>> members = [];

    public StructComparer(IStructSchema<T> schema)
    {
        schema.VisitMembers(this);
    }

    public void Visit<TValue>(IMemberSchema<T, TValue> member)
    {
        members.Add(
            new MemberComparer<T, TValue>(member, SchemaEqualityComparer.For(member.TargetSchema))
        );
    }

    public bool Equals(T? x, T? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        foreach (var member in members)
        {
            if (!member.Equals(x, y))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode([DisallowNull] T obj)
    {
        var hash = new HashCode();
        foreach (var member in members)
        {
            hash.Add(member.GetHashCode(obj));
        }

        return hash.ToHashCode();
    }
}

internal interface IMemberComparer<in TContainer>
{
    bool Equals(TContainer x, TContainer y);

    int GetHashCode(TContainer container);
}

internal sealed class MemberComparer<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    IEqualityComparer<TValue> comparer
) : IMemberComparer<TContainer>
{
    public bool Equals(TContainer x, TContainer y) =>
        comparer.Equals(member.GetValue(x), member.GetValue(y));

    public int GetHashCode(TContainer container) =>
        member.GetValue(container) is { } value ? comparer.GetHashCode(value) : 0;
}

internal sealed class UnionComparer<T> : IEqualityComparer<T>, IUnionCaseVisitor<T>
{
    private readonly List<IUnionCaseComparer<T>> cases = [];

    public UnionComparer(IUnionSchema<T> schema)
    {
        schema.VisitCases(this);
    }

    public void Visit<TValue>(IUnionCaseSchema<T, TValue> unionCase)
    {
        cases.Add(
            new UnionCaseComparer<T, TValue>(
                unionCase,
                SchemaEqualityComparer.For(unionCase.TargetSchema)
            )
        );
    }

    public bool Equals(T? x, T? y)
    {
        if (x is null || y is null)
        {
            return x is null && y is null;
        }

        foreach (var @case in cases)
        {
            if (@case.Matches(x))
            {
                return @case.Matches(y) && @case.Equals(x, y);
            }
        }

        return EqualityComparer<T>.Default.Equals(x, y);
    }

    public int GetHashCode([DisallowNull] T obj)
    {
        foreach (var @case in cases)
        {
            if (@case.Matches(obj))
            {
                return @case.GetHashCode(obj);
            }
        }

        return 0;
    }
}

internal interface IUnionCaseComparer<in T>
{
    bool Matches(T value);

    bool Equals(T x, T y);

    int GetHashCode(T value);
}

internal sealed class UnionCaseComparer<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IEqualityComparer<TValue> comparer
) : IUnionCaseComparer<TUnion>
{
    public bool Matches(TUnion value) => unionCase.Matches(value);

    public bool Equals(TUnion x, TUnion y) =>
        comparer.Equals(unionCase.GetValue(x), unionCase.GetValue(y));

    public int GetHashCode(TUnion value) =>
        HashCode.Combine(
            unionCase.Name,
            unionCase.GetValue(value) is { } inner ? comparer.GetHashCode(inner) : 0
        );
}

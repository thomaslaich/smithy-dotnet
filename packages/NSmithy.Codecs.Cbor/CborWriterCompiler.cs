using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

internal interface ICborValueWriter<in T>
{
    void Write(CborWriter writer, T value);
}

internal interface ICborMemberWriter<in TContainer>
{
    void Write(CborWriter writer, TContainer value);
}

internal interface ICborUnionCaseWriter<in TUnion>
{
    bool TryWrite(CborWriter writer, TUnion value);
}

internal sealed class CborWriterCompiler : ISchemaVisitor<object>
{
    private readonly SchemaCompilationCache cache = new();

    public static ICborValueWriter<T> Compile<T>(Schema<T> schema, bool materializeTopLevelDefaults)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CborWriterCompiler().CompileTopLevelValue(schema, materializeTopLevelDefaults);
    }

    private ICborValueWriter<T> CompileTopLevelValue<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults
    )
    {
        if (schema.Resolved is IStructSchema<T> structure)
        {
            return CompileStructure(structure, materializeTopLevelDefaults);
        }

        return CompileValue(schema);
    }

    public ICborValueWriter<T> CompileValue<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return cache.GetOrCompile<ICborValueWriter<T>, DeferredCborValueWriter<T>>(
            schema,
            static () => new DeferredCborValueWriter<T>(),
            target => (ICborValueWriter<T>)target.Accept(this)
        );
    }

    public object VisitBoolean(Schema<bool> schema) =>
        new DelegatingCborValueWriter<bool>(static (writer, value) => writer.WriteBoolean(value));

    public object VisitByte(Schema<sbyte> schema) =>
        new DelegatingCborValueWriter<sbyte>(static (writer, value) => writer.WriteInt32(value));

    public object VisitShort(Schema<short> schema) =>
        new DelegatingCborValueWriter<short>(static (writer, value) => writer.WriteInt32(value));

    public object VisitInteger(Schema<int> schema) =>
        new DelegatingCborValueWriter<int>(static (writer, value) => writer.WriteInt32(value));

    public object VisitLong(Schema<long> schema) =>
        new DelegatingCborValueWriter<long>(static (writer, value) => writer.WriteInt64(value));

    public object VisitFloat(Schema<float> schema) =>
        new DelegatingCborValueWriter<float>(static (writer, value) => writer.WriteSingle(value));

    public object VisitDouble(Schema<double> schema) =>
        new DelegatingCborValueWriter<double>(static (writer, value) => writer.WriteDouble(value));

    public object VisitBigInteger(Schema<BigInteger> schema) =>
        new DelegatingCborValueWriter<BigInteger>(WriteBigInteger);

    public object VisitBigDecimal(Schema<decimal> schema) =>
        new DelegatingCborValueWriter<decimal>(WriteBigDecimal);

    public object VisitString(Schema<string> schema) =>
        new DelegatingCborValueWriter<string>(
            static (writer, value) =>
            {
                if (value is null)
                    writer.WriteNull();
                else
                    writer.WriteTextString(value);
            }
        );

    public object VisitBlob(Schema<byte[]> schema) =>
        new DelegatingCborValueWriter<byte[]>(
            static (writer, value) =>
            {
                if (value is null)
                    writer.WriteNull();
                else
                    writer.WriteByteString(value);
            }
        );

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new DelegatingCborValueWriter<DateTimeOffset>(WriteTimestamp);

    public object VisitDocument(Schema<Document> schema) =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableCborValueWriter<T>(CompileValue(schema.TargetSchema));

    public object VisitStreamingBlob(Schema<Stream> schema) =>
        throw new NotSupportedException("CBOR codec does not support streaming blob schemas.");

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("CBOR codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => new ListCborValueWriter<TCollection, TElement>(schema, CompileValue(schema.ElementSchema));

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => new MapCborValueWriter<TDictionary, TValue>(schema, CompileValue(schema.ValueSchema));

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema)
    {
        return CompileStructure(schema, materializeDefaults: true);
    }

    private ICborValueWriter<T> CompileStructure<T>(
        IStructSchema<T> schema,
        bool materializeDefaults
    )
    {
        var visitor = new CborMemberWriterCompiler<T>(this, materializeDefaults);
        schema.VisitMembers(visitor);
        return schema.ValueSerializer is { } valueSerializer
            ? new DirectStructureCborValueWriter<T>(valueSerializer, visitor.Plans)
            : new FallbackStructureCborValueWriter<T>(visitor.Writers);
    }

    public object VisitUnion<T>(IUnionSchema<T> schema)
    {
        var visitor = new CborUnionCaseWriterCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionCborValueWriter<T>(visitor.Writers);
    }

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> =>
        new DelegatingCborValueWriter<T>(
            static (writer, value) => writer.WriteTextString(value.Value)
        );

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new IntEnumCborValueWriter<T>(schema);
}

internal sealed class DeferredCborValueWriter<T>
    : ICborValueWriter<T>,
        IDeferredCompilation<ICborValueWriter<T>>
{
    public void Complete(ICborValueWriter<T> compiled) => Set(compiled);

    private ICborValueWriter<T>? inner;

    public void Set(ICborValueWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        inner = writer;
    }

    public void Write(CborWriter writer, T value)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("CBOR writer has not been initialized.");
        }

        inner.Write(writer, value);
    }
}

internal sealed class DelegatingCborValueWriter<T>(Action<CborWriter, T> write)
    : ICborValueWriter<T>
{
    public void Write(CborWriter writer, T value) => write(writer, value);
}

internal sealed class NullableCborValueWriter<T>(ICborValueWriter<T> inner) : ICborValueWriter<T?>
    where T : struct
{
    public void Write(CborWriter writer, T? value)
    {
        if (value.HasValue)
            inner.Write(writer, value.Value);
        else
            writer.WriteNull();
    }
}

internal sealed class IntEnumCborValueWriter<T>(IntEnumSchema<T> schema) : ICborValueWriter<T>
    where T : struct, Enum
{
    public void Write(CborWriter writer, T value) =>
        writer.WriteInt32(schema.GetIntegerValue(value));
}

internal sealed class CborMemberWriterCompiler<TContainer>(
    CborWriterCompiler compiler,
    bool materializeDefaults,
    ISet<IMemberSchema<TContainer>>? includedMembers = null
) : IMemberVisitor<TContainer>
{
    private readonly List<ICborMemberWriter<TContainer>> writers = [];
    private readonly List<object?> plans = [];

    // An array, not the interface: the write path iterates this once per object, and
    // foreach over IReadOnlyList<T> goes through IEnumerable<T>.GetEnumerator, which
    // boxes List<T>.Enumerator — a heap allocation per structure written.
    public ICborMemberWriter<TContainer>[] Writers => [.. writers];

    public object?[] Plans => [.. plans];

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        if (includedMembers is not null && !includedMembers.Contains(member))
        {
            plans.Add(null);
            return;
        }

        var plan = new CborMemberPlan<TValue>(
            member,
            compiler.CompileValue(member.TargetSchema),
            materializeDefaults
        );
        plans.Add(plan);
        writers.Add(new CborMemberWriter<TContainer, TValue>(member, plan));
    }
}

internal sealed class CborMemberCollector<TContainer> : IMemberVisitor<TContainer>
{
    public ISet<IMemberSchema<TContainer>> Members { get; } =
        new HashSet<IMemberSchema<TContainer>>(ReferenceEqualityComparer.Instance);

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member) => Members.Add(member);
}

internal sealed class CborMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    CborMemberPlan<TValue> plan
) : ICborMemberWriter<TContainer>
{
    public void Write(CborWriter writer, TContainer value) =>
        plan.Write(writer, member.GetValue(value));
}

internal sealed class CborMemberPlan<TValue>(
    ITargetedMemberSchema<TValue> member,
    ICborValueWriter<TValue> valueWriter,
    bool materializeDefault
)
{
    private readonly string name = member.Name;
    private readonly bool isRequired = member.IsRequired;

    // Constant per member, so resolved at compile time rather than per write.
    private readonly (bool Present, TValue? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(CborWriter writer, TValue memberValue)
    {
        if (memberValue is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            memberValue = memberDefault.Value!;
        }

        writer.WriteTextString(name);
        valueWriter.Write(writer, memberValue);
    }
}

internal readonly struct CborStructMemberWriter(CborWriter writer, object?[] memberPlans)
    : IStructMemberWriter
{
    public void WriteMember<TValue>(int index, TValue value)
    {
        if (memberPlans[index] is { } plan)
        {
            ((CborMemberPlan<TValue>)plan).Write(writer, value);
        }
    }
}

internal sealed class DirectStructureCborValueWriter<T>(
    IStructValueSerializer<T> valueSerializer,
    object?[] memberPlans
) : ICborValueWriter<T>
{
    public void Write(CborWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartMap(null);
        var memberWriter = new CborStructMemberWriter(writer, memberPlans);
        valueSerializer.WriteMembers(value, ref memberWriter);
        writer.WriteEndMap();
    }
}

internal sealed class FallbackStructureCborValueWriter<T>(ICborMemberWriter<T>[] memberWriters)
    : ICborValueWriter<T>
{
    public void Write(CborWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartMap(null);
        foreach (var memberWriter in memberWriters)
        {
            memberWriter.Write(writer, value);
        }

        writer.WriteEndMap();
    }
}

internal sealed class CborUnionCaseWriterCompiler<TUnion>(CborWriterCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<ICborUnionCaseWriter<TUnion>> writers = [];

    public ICborUnionCaseWriter<TUnion>[] Writers => [.. writers];

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> @case)
    {
        writers.Add(
            new CborUnionCaseWriter<TUnion, TValue>(
                @case,
                compiler.CompileValue(@case.TargetSchema)
            )
        );
    }
}

internal sealed class CborUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> @case,
    ICborValueWriter<TValue> valueWriter
) : ICborUnionCaseWriter<TUnion>
{
    public bool TryWrite(CborWriter writer, TUnion value)
    {
        if (!@case.Matches(value))
        {
            return false;
        }

        writer.WriteTextString(@case.Name);
        valueWriter.Write(writer, @case.GetValue(value));
        return true;
    }
}

internal sealed class UnionCborValueWriter<T>(ICborUnionCaseWriter<T>[] caseWriters)
    : ICborValueWriter<T>
{
    public void Write(CborWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartMap(1);
        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(writer, value))
            {
                writer.WriteEndMap();
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}

internal sealed class ListCborValueWriter<TCollection, TElement>(
    IListSchema<TCollection, TElement> schema,
    ICborValueWriter<TElement> elementWriter
) : ICborValueWriter<TCollection>
{
    public void Write(CborWriter writer, TCollection value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        var elements = schema.GetElements(value);
        if (!elements.TryGetNonEnumeratedCount(out var count))
        {
            var materialized = elements.ToArray();
            elements = materialized;
            count = materialized.Length;
        }

        writer.WriteStartArray(count);
        foreach (var element in elements)
        {
            elementWriter.Write(writer, element);
        }

        writer.WriteEndArray();
    }
}

internal sealed class MapCborValueWriter<TDictionary, TValue>(
    IMapSchema<TDictionary, TValue> schema,
    ICborValueWriter<TValue> valueWriter
) : ICborValueWriter<TDictionary>
{
    public void Write(CborWriter writer, TDictionary value)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        var entries = schema.GetEntries(value);
        if (!entries.TryGetNonEnumeratedCount(out var count))
        {
            var materialized = entries.ToArray();
            entries = materialized;
            count = materialized.Length;
        }

        writer.WriteStartMap(count);
        foreach (var entry in entries)
        {
            writer.WriteTextString(entry.Key);
            valueWriter.Write(writer, entry.Value);
        }

        writer.WriteEndMap();
    }
}

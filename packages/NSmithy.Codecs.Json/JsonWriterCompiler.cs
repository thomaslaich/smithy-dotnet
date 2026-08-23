using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Json.JsonWire;

namespace NSmithy.Codecs.Json;

internal interface IJsonValueWriter<in T>
{
    void Write(Utf8JsonWriter writer, T value);
}

internal interface IJsonMemberWriter<in TContainer>
{
    void Write(Utf8JsonWriter writer, TContainer container);
}

internal interface IJsonUnionCaseWriter<in TUnion>
{
    bool TryWrite(Utf8JsonWriter writer, TUnion value);
}

internal sealed class JsonWriterCompiler : ISchemaVisitor<object>
{
    private readonly SchemaCompilationCache cache = new();

    public static IJsonValueWriter<T> Compile<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new JsonWriterCompiler().CompileTopLevelValue(schema, materializeTopLevelDefaults);
    }

    private IJsonValueWriter<T> CompileTopLevelValue<T>(
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

    public static IJsonValueWriter<T> Compile<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var compiler = new JsonWriterCompiler();
        return compiler.CompileProjection(projection, materializeTopLevelDefaults);
    }

    public IJsonValueWriter<T> CompileValue<T>(Schema<T> schema)
    {
        return cache.GetOrCompile<IJsonValueWriter<T>, DeferredJsonValueWriter<T>>(
            schema,
            static () => new DeferredJsonValueWriter<T>(),
            target => CompileValueCore<T>(target)
        );
    }

    public IJsonValueWriter<T> CompileValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> memberTraits
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(memberTraits);
        if (memberTraits.Count == 0)
        {
            return CompileValue(schema);
        }

        return (IJsonValueWriter<T>)
            schema.Resolved.Accept(new MemberTraitJsonWriterCompiler(this, memberTraits));
    }

    internal IJsonValueWriter<T> CompileValueCore<T>(Schema resolved) =>
        (IJsonValueWriter<T>)resolved.Accept(this);

    public object VisitBoolean(Schema<bool> schema) => new BooleanJsonValueWriter();

    public object VisitByte(Schema<sbyte> schema) => new ByteJsonValueWriter();

    public object VisitShort(Schema<short> schema) => new ShortJsonValueWriter();

    public object VisitInteger(Schema<int> schema) => new IntegerJsonValueWriter();

    public object VisitLong(Schema<long> schema) => new LongJsonValueWriter();

    public object VisitFloat(Schema<float> schema) => new FloatJsonValueWriter();

    public object VisitDouble(Schema<double> schema) => new DoubleJsonValueWriter();

    public object VisitBigInteger(Schema<BigInteger> schema) => new BigIntegerJsonValueWriter();

    public object VisitBigDecimal(Schema<decimal> schema) => new BigDecimalJsonValueWriter();

    public object VisitString(Schema<string> schema) => new StringJsonValueWriter();

    public object VisitBlob(Schema<byte[]> schema) => new BlobJsonValueWriter();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new TimestampJsonValueWriter(TimestampFormat.Resolve(null, schema));

    public object VisitDocument(Schema<Document> schema) => new DocumentJsonValueWriter();

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct => CompileNullable(schema);

    public object VisitStreamingBlob(Schema<Stream> schema) =>
        throw new NotSupportedException("JSON codec does not support streaming blob schemas.");

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        throw new NotSupportedException("JSON codec does not support event stream schemas.");

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => CompileList(schema);

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => CompileMap(schema);

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
        CompileStructure(schema);

    public object VisitUnion<T>(IUnionSchema<T> schema) => CompileUnion(schema);

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => CompileStringEnum(schema);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => CompileIntEnum(schema);

    internal NullableJsonValueWriter<T> CompileNullable<T>(NullableSchema<T> schema)
        where T : struct => new NullableJsonValueWriter<T>(CompileValue(schema.TargetSchema));

    internal static StringEnumJsonValueWriter<T> CompileStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => new StringEnumJsonValueWriter<T>();

    internal static IntEnumJsonValueWriter<T> CompileIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new IntEnumJsonValueWriter<T>(schema);

    internal IJsonValueWriter<T> CompileStructure<T>(IStructSchema<T> schema) =>
        CompileStructure(schema, materializeDefaults: true);

    private IJsonValueWriter<T> CompileStructure<T>(
        IStructSchema<T> schema,
        bool materializeDefaults
    )
    {
        var visitor = new JsonMemberWriterCompiler<T>(this, materializeDefaults);
        schema.VisitMembers(visitor);
        return schema.ValueSerializer is { } valueSerializer
            ? new DirectStructureJsonValueWriter<T>(valueSerializer, visitor.Plans)
            : new FallbackStructureJsonValueWriter<T>(visitor.Writers);
    }

    internal IJsonValueWriter<T> CompileProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults
    )
    {
        if (projection.Source.ValueSerializer is not { } valueSerializer)
        {
            var fallback = new JsonMemberWriterCompiler<T>(this, materializeTopLevelDefaults);
            projection.VisitMembers(fallback);
            return new FallbackStructureJsonValueWriter<T>(fallback.Writers);
        }

        var included = new JsonMemberCollector<T>();
        projection.VisitMembers(included);
        var visitor = new JsonMemberWriterCompiler<T>(
            this,
            materializeTopLevelDefaults,
            included.Members
        );
        projection.Source.VisitMembers(visitor);
        return new DirectStructureJsonValueWriter<T>(valueSerializer, visitor.Plans);
    }

    internal ListJsonValueWriter<TCollection, TElement> CompileList<TCollection, TElement>(
        IListSchema<TCollection, TElement> schema
    ) =>
        new ListJsonValueWriter<TCollection, TElement>(
            schema,
            CompileValue(
                schema.TypedElementMember.TargetSchema,
                schema.TypedElementMember.MemberTraits
            )
        );

    internal MapJsonValueWriter<TDictionary, TValue> CompileMap<TDictionary, TValue>(
        IMapSchema<TDictionary, TValue> schema
    ) =>
        new MapJsonValueWriter<TDictionary, TValue>(
            schema,
            CompileValue(schema.TypedValueMember.TargetSchema, schema.TypedValueMember.MemberTraits)
        );

    internal IJsonValueWriter<T> CompileUnion<T>(IUnionSchema<T> schema)
    {
        if (IsOpenUnion(schema))
        {
            var openVisitor = new JsonOpenUnionCaseWriterCompiler<T>(this);
            schema.VisitCases(openVisitor);
            TryGetDiscriminatorName(schema, out var discriminatorName);
            return new OpenUnionJsonValueWriter<T>(openVisitor.Writers, discriminatorName);
        }

        var visitor = new JsonUnionCaseWriterCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionJsonValueWriter<T>(visitor.Writers);
    }

    internal static IJsonValueWriter<T> Cast<T>(object writer) => (IJsonValueWriter<T>)writer;
}

internal sealed class DeferredJsonValueWriter<T>
    : IJsonValueWriter<T>,
        IDeferredCompilation<IJsonValueWriter<T>>
{
    public void Complete(IJsonValueWriter<T> compiled) => Set(compiled);

    internal IJsonValueWriter<T>? inner;

    public void Set(IJsonValueWriter<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        inner = writer;
    }

    public void Write(Utf8JsonWriter writer, T value)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("JSON writer has not been initialized.");
        }

        inner.Write(writer, value);
    }
}

internal sealed class DelegatingJsonValueWriter<T>(Action<Utf8JsonWriter, T> write)
    : IJsonValueWriter<T>
{
    public void Write(Utf8JsonWriter writer, T value) => write(writer, value);
}

internal sealed class MemberTraitJsonWriterCompiler(
    JsonWriterCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> memberTraits
) : ISchemaVisitor<object>
{
    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new TimestampJsonValueWriter(TimestampFormat.Resolve(memberTraits, schema));

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableJsonValueWriter<T>(inner.CompileValue(schema.TargetSchema, memberTraits));

    public object VisitBoolean(Schema<bool> schema) => inner.CompileValue(schema);

    public object VisitByte(Schema<sbyte> schema) => inner.CompileValue(schema);

    public object VisitShort(Schema<short> schema) => inner.CompileValue(schema);

    public object VisitInteger(Schema<int> schema) => inner.CompileValue(schema);

    public object VisitLong(Schema<long> schema) => inner.CompileValue(schema);

    public object VisitFloat(Schema<float> schema) => inner.CompileValue(schema);

    public object VisitDouble(Schema<double> schema) => inner.CompileValue(schema);

    public object VisitBigInteger(Schema<BigInteger> schema) => inner.CompileValue(schema);

    public object VisitBigDecimal(Schema<decimal> schema) => inner.CompileValue(schema);

    public object VisitString(Schema<string> schema) => inner.CompileValue(schema);

    public object VisitBlob(Schema<byte[]> schema) => inner.CompileValue(schema);

    public object VisitDocument(Schema<Document> schema) => inner.CompileValue(schema);

    public object VisitStreamingBlob(Schema<Stream> schema) => inner.CompileValue(schema);

    public object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
        inner.CompileValue(schema);

    public object VisitList<TCollection, TElement, TBuilder>(
        IListSchema<TCollection, TElement, TBuilder> schema
    ) => inner.CompileValue((Schema<TCollection>)schema);

    public object VisitMap<TDictionary, TValue, TBuilder>(
        IMapSchema<TDictionary, TValue, TBuilder> schema
    ) => inner.CompileValue((Schema<TDictionary>)schema);

    public object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
        inner.CompileValue((Schema<T>)schema);

    public object VisitUnion<T>(IUnionSchema<T> schema) => inner.CompileValue((Schema<T>)schema);

    public object VisitStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => inner.CompileValue(schema);

    public object VisitIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => inner.CompileValue(schema);
}

internal sealed class JsonMemberWriterCompiler<TContainer>(
    JsonWriterCompiler compiler,
    bool materializeDefaults,
    ISet<IMemberSchema<TContainer>>? includedMembers = null
) : IMemberVisitor<TContainer>
{
    private readonly List<IJsonMemberWriter<TContainer>> writers = [];
    private readonly List<object?> plans = [];

    // An array, not the interface: the write path iterates this once per object, and
    // foreach over IReadOnlyList<T> goes through IEnumerable<T>.GetEnumerator, which
    // boxes List<T>.Enumerator — a heap allocation per structure written.
    public IJsonMemberWriter<TContainer>[] Writers => [.. writers];

    public object?[] Plans => [.. plans];

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member)
    {
        if (includedMembers is not null && !includedMembers.Contains(member))
        {
            plans.Add(null);
            return;
        }

        var plan = new JsonMemberPlan<TValue>(
            member,
            compiler.CompileValue(member.TargetSchema, member.MemberTraits),
            materializeDefaults
        );
        plans.Add(plan);
        writers.Add(new JsonMemberWriter<TContainer, TValue>(member, plan));
    }
}

internal sealed class JsonMemberCollector<TContainer> : IMemberVisitor<TContainer>
{
    public ISet<IMemberSchema<TContainer>> Members { get; } =
        new HashSet<IMemberSchema<TContainer>>(ReferenceEqualityComparer.Instance);

    public void Visit<TValue>(IMemberSchema<TContainer, TValue> member) => Members.Add(member);
}

internal sealed class JsonMemberWriter<TContainer, TValue>(
    IMemberSchema<TContainer, TValue> member,
    JsonMemberPlan<TValue> plan
) : IJsonMemberWriter<TContainer>
{
    public void Write(Utf8JsonWriter writer, TContainer container) =>
        plan.Write(writer, member.GetValue(container));
}

internal sealed class JsonMemberPlan<TValue>(
    ITargetedMemberSchema<TValue> member,
    IJsonValueWriter<TValue> valueWriter,
    bool materializeDefault
)
{
    // Resolved once at compile time rather than per write. Previously each member
    // of each object cost a ShapeId-keyed trait lookup to find any @jsonName, then
    // handed a string to WritePropertyName, which re-transcoded and re-escaped it
    // every time. The wire name is constant per member, so all of that is
    // hoistable — which is what System.Text.Json's source generator does.
    private readonly JsonEncodedText propertyName = JsonEncodedText.Encode(
        WireName(member.MemberTraits, member.Name)
    );

    private readonly bool isRequired = member.IsRequired;

    // Same reasoning as the wire name: whether this member materializes a default,
    // and what that default is, are constant per member. Previously every optional
    // member that happened to be null cost two more trait lookups per object.
    private readonly (bool Present, TValue? Value) memberDefault = ResolveDefault(
        member.TargetSchema,
        member.MemberTraits,
        materializeDefault
    );

    public void Write(Utf8JsonWriter writer, TValue value)
    {
        if (value is null && !isRequired)
        {
            if (!memberDefault.Present)
            {
                return;
            }

            value = memberDefault.Value!;
        }

        writer.WritePropertyName(propertyName);
        valueWriter.Write(writer, value);
    }
}

internal sealed class JsonUnionCaseWriterCompiler<TUnion>(JsonWriterCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IJsonUnionCaseWriter<TUnion>> writers = [];

    public IJsonUnionCaseWriter<TUnion>[] Writers => [.. writers];

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> @case)
    {
        writers.Add(
            new JsonUnionCaseWriter<TUnion, TValue>(
                @case,
                compiler.CompileValue(@case.TargetSchema)
            )
        );
    }
}

internal sealed class JsonUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> @case,
    IJsonValueWriter<TValue> valueWriter
) : IJsonUnionCaseWriter<TUnion>
{
    public bool TryWrite(Utf8JsonWriter writer, TUnion value)
    {
        if (!@case.Matches(value))
        {
            return false;
        }

        writer.WritePropertyName(WireName(@case.Traits, @case.Name));
        valueWriter.Write(writer, @case.GetValue(value));
        return true;
    }
}

internal sealed class JsonOpenUnionCaseWriterCompiler<TUnion>(JsonWriterCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IJsonOpenUnionCaseWriter<TUnion>> writers = [];

    public IJsonOpenUnionCaseWriter<TUnion>[] Writers => [.. writers];

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> @case)
    {
        writers.Add(
            new JsonOpenUnionCaseWriter<TUnion, TValue>(
                @case,
                compiler.CompileValue(@case.TargetSchema),
                IsJsonUnknownCase(@case)
            )
        );
    }
}

internal interface IJsonOpenUnionCaseWriter<in TUnion>
{
    bool TryWrite(Utf8JsonWriter writer, TUnion value, string discriminatorName);
}

internal sealed class JsonOpenUnionCaseWriter<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> @case,
    IJsonValueWriter<TValue> valueWriter,
    bool isUnknown
) : IJsonOpenUnionCaseWriter<TUnion>
{
    public bool TryWrite(Utf8JsonWriter writer, TUnion value, string discriminatorName)
    {
        if (!@case.Matches(value))
        {
            return false;
        }

        var caseValue = @case.GetValue(value);
        if (isUnknown)
        {
            valueWriter.Write(writer, caseValue);
            return true;
        }

        if (discriminatorName.Length == 0)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(WireName(@case.Traits, @case.Name));
            valueWriter.Write(writer, caseValue);
            writer.WriteEndObject();
            return true;
        }

        writer.WriteStartObject();
        writer.WriteString(discriminatorName, WireName(@case.Traits, @case.Name));
        using var buffer = new MemoryStream();
        using (var bufferedWriter = new Utf8JsonWriter(buffer))
        {
            valueWriter.Write(bufferedWriter, caseValue);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals(discriminatorName))
                {
                    property.WriteTo(writer);
                }
            }
        }
        else
        {
            writer.WritePropertyName("value");
            document.RootElement.WriteTo(writer);
        }

        writer.WriteEndObject();
        return true;
    }
}

internal readonly struct JsonStructMemberWriter(Utf8JsonWriter writer, object?[] memberPlans)
    : IStructMemberWriter
{
    public void WriteMember<TValue>(int index, TValue value)
    {
        var plan = memberPlans[index];
        if (plan is not null)
        {
            ((JsonMemberPlan<TValue>)plan).Write(writer, value);
        }
    }
}

internal sealed class DirectStructureJsonValueWriter<T>(
    IStructValueSerializer<T> valueSerializer,
    object?[] memberPlans
) : IJsonValueWriter<T>
{
    public void Write(Utf8JsonWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        var memberWriter = new JsonStructMemberWriter(writer, memberPlans);
        valueSerializer.WriteMembers(value, ref memberWriter);
        writer.WriteEndObject();
    }
}

internal sealed class FallbackStructureJsonValueWriter<T>(IJsonMemberWriter<T>[] memberWriters)
    : IJsonValueWriter<T>
{
    public void Write(Utf8JsonWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var memberWriter in memberWriters)
        {
            memberWriter.Write(writer, value);
        }
        writer.WriteEndObject();
    }
}

internal sealed class UnionJsonValueWriter<T>(IJsonUnionCaseWriter<T>[] caseWriters)
    : IJsonValueWriter<T>
{
    public void Write(Utf8JsonWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(writer, value))
            {
                writer.WriteEndObject();
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}

internal sealed class OpenUnionJsonValueWriter<T>(
    IJsonOpenUnionCaseWriter<T>[] caseWriters,
    string discriminatorName
) : IJsonValueWriter<T>
{
    public void Write(Utf8JsonWriter writer, T value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        foreach (var caseWriter in caseWriters)
        {
            if (caseWriter.TryWrite(writer, value, discriminatorName))
            {
                return;
            }
        }

        throw new InvalidOperationException($"No union case matched '{typeof(T).Name}'.");
    }
}

internal sealed class ListJsonValueWriter<TCollection, TElement>(
    IListSchema<TCollection, TElement> schema,
    IJsonValueWriter<TElement> elementWriter
) : IJsonValueWriter<TCollection>
{
    public void Write(Utf8JsonWriter writer, TCollection value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        var elements = schema.GetElements(value);
        if (elements is IReadOnlyList<TElement> list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                elementWriter.Write(writer, list[index]);
            }
        }
        else
        {
            foreach (var element in elements)
            {
                elementWriter.Write(writer, element);
            }
        }
        writer.WriteEndArray();
    }
}

internal sealed class MapJsonValueWriter<TDictionary, TValue>(
    IMapSchema<TDictionary, TValue> schema,
    IJsonValueWriter<TValue> valueWriter
) : IJsonValueWriter<TDictionary>
{
    public void Write(Utf8JsonWriter writer, TDictionary value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var entry in schema.GetEntries(value))
        {
            writer.WritePropertyName(entry.Key);
            valueWriter.Write(writer, entry.Value);
        }
        writer.WriteEndObject();
    }
}

internal sealed class NullableJsonValueWriter<T>(IJsonValueWriter<T> inner) : IJsonValueWriter<T?>
    where T : struct
{
    public void Write(Utf8JsonWriter writer, T? value)
    {
        if (value.HasValue)
        {
            inner.Write(writer, value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

internal sealed class BooleanJsonValueWriter : IJsonValueWriter<bool>
{
    public void Write(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);
}

internal sealed class ByteJsonValueWriter : IJsonValueWriter<sbyte>
{
    public void Write(Utf8JsonWriter writer, sbyte value) => writer.WriteNumberValue(value);
}

internal sealed class ShortJsonValueWriter : IJsonValueWriter<short>
{
    public void Write(Utf8JsonWriter writer, short value) => writer.WriteNumberValue(value);
}

internal sealed class IntegerJsonValueWriter : IJsonValueWriter<int>
{
    public void Write(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);
}

internal sealed class LongJsonValueWriter : IJsonValueWriter<long>
{
    public void Write(Utf8JsonWriter writer, long value) => writer.WriteNumberValue(value);
}

internal sealed class FloatJsonValueWriter : IJsonValueWriter<float>
{
    public void Write(Utf8JsonWriter writer, float value) => WriteFloat(writer, value);
}

internal sealed class DoubleJsonValueWriter : IJsonValueWriter<double>
{
    public void Write(Utf8JsonWriter writer, double value) => WriteDouble(writer, value);
}

internal sealed class BigIntegerJsonValueWriter : IJsonValueWriter<BigInteger>
{
    public void Write(Utf8JsonWriter writer, BigInteger value) =>
        writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture), true);
}

internal sealed class BigDecimalJsonValueWriter : IJsonValueWriter<decimal>
{
    public void Write(Utf8JsonWriter writer, decimal value) => writer.WriteNumberValue(value);
}

internal sealed class StringJsonValueWriter : IJsonValueWriter<string>
{
    public void Write(Utf8JsonWriter writer, string value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}

internal sealed class StringEnumJsonValueWriter<T> : IJsonValueWriter<T>
    where T : IStringEnumValue<T>
{
    public void Write(Utf8JsonWriter writer, T value) => writer.WriteStringValue(value.Value);
}

internal sealed class IntEnumJsonValueWriter<T>(IntEnumSchema<T> schema) : IJsonValueWriter<T>
    where T : struct, Enum
{
    public void Write(Utf8JsonWriter writer, T value) =>
        writer.WriteNumberValue(schema.GetIntegerValue(value));
}

internal sealed class BlobJsonValueWriter : IJsonValueWriter<byte[]>
{
    public void Write(Utf8JsonWriter writer, byte[] value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteBase64StringValue(value);
    }
}

internal sealed class TimestampJsonValueWriter(string format) : IJsonValueWriter<DateTimeOffset>
{
    public void Write(Utf8JsonWriter writer, DateTimeOffset value) =>
        TimestampFormat.Write(writer, value, format);
}

internal sealed class DocumentJsonValueWriter : IJsonValueWriter<Document>
{
    public void Write(Utf8JsonWriter writer, Document value) =>
        DocumentJsonWriter.Write(writer, value);
}

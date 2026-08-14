using System.Globalization;
using System.Numerics;
using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Json.JsonWire;

namespace NSmithy.Codecs.Json;

internal interface IJsonValueReader<T>
{
    T Read(JsonElement value);
}

internal interface IJsonMemberReader<in TBuilder>
{
    string Name { get; }

    bool IsRequired { get; }

    void ReadMissing(TBuilder builder);

    void ReadInto(TBuilder builder, JsonElement value);
}

internal interface IJsonUnionCaseReader<out TUnion>
{
    string Name { get; }

    TUnion Read(JsonElement value);
}

internal sealed class JsonReaderCompiler(WireReadMode readMode) : ISchemaVisitor<object>
{
    private readonly SchemaCompilationCache cache = new();

    public WireReadMode ReadMode => readMode;

    public static IJsonValueReader<T> Compile<T>(Schema<T> schema, WireReadMode readMode)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new JsonReaderCompiler(readMode).CompileValue(schema);
    }

    public static StructureJsonProjectionReader<TBuilder> Compile<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        WireReadMode readMode
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        var compiler = new JsonReaderCompiler(readMode);
        var visitor = new JsonMemberReaderCompiler<T, TBuilder>(compiler);
        projection.VisitMembers(visitor);
        return new StructureJsonProjectionReader<TBuilder>(visitor.Readers);
    }

    public IJsonValueReader<T> CompileValue<T>(Schema<T> schema)
    {
        return cache.GetOrCompile<IJsonValueReader<T>, DeferredJsonValueReader<T>>(
            schema,
            static () => new DeferredJsonValueReader<T>(),
            target => CompileValueCore<T>(target)
        );
    }

    public IJsonValueReader<T> CompileValue<T>(
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

        return (IJsonValueReader<T>)
            schema.Resolved.Accept(new MemberTraitJsonReaderCompiler(this, memberTraits));
    }

    private IJsonValueReader<T> CompileValueCore<T>(Schema resolved) =>
        (IJsonValueReader<T>)resolved.Accept(this);

    public object VisitBoolean(Schema<bool> schema) => new BooleanJsonValueReader();

    public object VisitByte(Schema<sbyte> schema) => new ByteJsonValueReader();

    public object VisitShort(Schema<short> schema) => new ShortJsonValueReader();

    public object VisitInteger(Schema<int> schema) => new IntegerJsonValueReader();

    public object VisitLong(Schema<long> schema) => new LongJsonValueReader();

    public object VisitFloat(Schema<float> schema) => new FloatJsonValueReader();

    public object VisitDouble(Schema<double> schema) => new DoubleJsonValueReader();

    public object VisitBigInteger(Schema<BigInteger> schema) => new BigIntegerJsonValueReader();

    public object VisitBigDecimal(Schema<decimal> schema) => new BigDecimalJsonValueReader();

    public object VisitString(Schema<string> schema) => new StringJsonValueReader();

    public object VisitBlob(Schema<byte[]> schema) => new BlobJsonValueReader();

    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new TimestampJsonValueReader(TimestampFormat.Resolve(null, schema), readMode);

    public object VisitDocument(Schema<Document> schema) => new DocumentJsonValueReader();

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

    private NullableJsonValueReader<T> CompileNullable<T>(NullableSchema<T> schema)
        where T : struct => new(CompileValue(schema.TargetSchema));

    private static StringEnumJsonValueReader<T> CompileStringEnum<T>(StringEnumSchema<T> schema)
        where T : IStringEnumValue<T> => new(schema);

    private static IntEnumJsonValueReader<T> CompileIntEnum<T>(IntEnumSchema<T> schema)
        where T : struct, Enum => new(schema);

    internal StructureJsonValueReader<T, TBuilder> CompileStructure<T, TBuilder>(
        IStructSchema<T, TBuilder> schema
    )
    {
        var visitor = new JsonMemberReaderCompiler<T, TBuilder>(this);
        schema.VisitMembers(visitor);
        return new StructureJsonValueReader<T, TBuilder>(
            schema.CreateTypedBuilder,
            schema.Build,
            visitor.Readers
        );
    }

    internal ListJsonValueReader<TCollection, TElement, TBuilder> CompileList<
        TCollection,
        TElement,
        TBuilder
    >(IListSchema<TCollection, TElement, TBuilder> schema) =>
        new(
            schema,
            CompileValue(
                schema.TypedElementMember.TargetSchema,
                schema.TypedElementMember.MemberTraits
            )
        );

    internal MapJsonValueReader<TDictionary, TValue, TBuilder> CompileMap<
        TDictionary,
        TValue,
        TBuilder
    >(IMapSchema<TDictionary, TValue, TBuilder> schema) =>
        new(
            schema,
            CompileValue(schema.TypedValueMember.TargetSchema, schema.TypedValueMember.MemberTraits)
        );

    private IJsonValueReader<T> CompileUnion<T>(IUnionSchema<T> schema)
    {
        if (IsOpenUnion(schema))
        {
            var openVisitor = new JsonOpenUnionCaseReaderCompiler<T>(this);
            schema.VisitCases(openVisitor);
            TryGetDiscriminatorName(schema, out var discriminatorName);
            return new OpenUnionJsonValueReader<T>(
                openVisitor.Readers,
                openVisitor.UnknownReader,
                discriminatorName
            );
        }

        var visitor = new JsonUnionCaseReaderCompiler<T>(this);
        schema.VisitCases(visitor);
        return new UnionJsonValueReader<T>(visitor.Readers);
    }

    internal static IJsonValueReader<T> Cast<T>(object reader) => (IJsonValueReader<T>)reader;
}

internal sealed class DeferredJsonValueReader<T>
    : IJsonValueReader<T>,
        IDeferredCompilation<IJsonValueReader<T>>
{
    public void Complete(IJsonValueReader<T> compiled) => Set(compiled);

    private IJsonValueReader<T>? inner;

    public void Set(IJsonValueReader<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        inner = reader;
    }

    public T Read(JsonElement value)
    {
        if (inner is null)
        {
            throw new InvalidOperationException("JSON reader has not been initialized.");
        }

        return inner.Read(value);
    }
}

internal sealed class DelegatingJsonValueReader<T>(Func<JsonElement, T> read) : IJsonValueReader<T>
{
    public T Read(JsonElement value) => read(value);
}

internal sealed class MemberTraitJsonReaderCompiler(
    JsonReaderCompiler inner,
    IReadOnlyDictionary<ShapeId, Trait> memberTraits
) : ISchemaVisitor<object>
{
    public object VisitTimestamp(Schema<DateTimeOffset> schema) =>
        new TimestampJsonValueReader(TimestampFormat.Resolve(memberTraits, schema), inner.ReadMode);

    public object VisitNullable<T>(NullableSchema<T> schema)
        where T : struct =>
        new NullableJsonValueReader<T>(inner.CompileValue(schema.TargetSchema, memberTraits));

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

internal sealed class JsonMemberReaderCompiler<TContainer, TBuilder>(JsonReaderCompiler compiler)
    : IMemberVisitor<TContainer, TBuilder>
{
    private readonly List<IJsonMemberReader<TBuilder>> readers = [];

    public IReadOnlyList<IJsonMemberReader<TBuilder>> Readers => readers;

    public void Visit<TValue>(IMemberSchema<TContainer, TBuilder, TValue> member)
    {
        readers.Add(
            new JsonMemberReader<TContainer, TBuilder, TValue>(
                member,
                compiler.CompileValue(member.TargetSchema, member.MemberTraits)
            )
        );
    }
}

internal sealed class JsonMemberReader<TContainer, TBuilder, TValue>(
    IMemberSchema<TContainer, TBuilder, TValue> member,
    IJsonValueReader<TValue> valueReader
) : IJsonMemberReader<TBuilder>
{
    public string Name => WireName(member.MemberTraits, member.Name);

    public bool IsRequired => member.IsRequired;

    public void ReadMissing(TBuilder builder)
    {
        if (
            TryCreateDefaultValue(
                member.TargetSchema,
                member.MemberTraits,
                out TValue? defaultValue
            )
        )
        {
            member.SetValue(builder, defaultValue!);
        }
    }

    public void ReadInto(TBuilder builder, JsonElement value)
    {
        member.SetValue(builder, valueReader.Read(value));
    }
}

internal sealed class StructureJsonValueReader<T, TBuilder>(
    Func<TBuilder> createBuilder,
    Func<TBuilder, T> build,
    IReadOnlyList<IJsonMemberReader<TBuilder>> memberReaders
) : IJsonValueReader<T>
{
    // Member names as UTF-8, encoded once. Matching a property against these never
    // transcodes and never allocates a name string.
    private readonly byte[][] utf8Names =
    [
        .. memberReaders.Select(reader => System.Text.Encoding.UTF8.GetBytes(reader.Name)),
    ];

    public T Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        var builder = createBuilder();

        // One pass over the payload, rather than one pass per member. The previous
        // shape called TryGetProperty for each member, and each of those calls
        // scanned the object again — so a structure with N members over an object
        // with M properties walked the document N times.
        Span<bool> seen =
            memberReaders.Count <= 64 ? stackalloc bool[64] : new bool[memberReaders.Count];

        foreach (var property in value.EnumerateObject())
        {
            var index = IndexOf(property);
            if (index < 0)
            {
                continue;
            }

            // First occurrence wins, which is what TryGetProperty did for a payload
            // carrying a duplicate key.
            if (seen[index])
            {
                continue;
            }

            var memberReader = memberReaders[index];

            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                // Left unseen so the trailing loop applies the modelled default, the
                // same as an absent member. A member carrying @default always has a
                // value in Smithy, so an explicit null must not leave it unset — and
                // the trailing loop raises the same error for a required member.
                continue;
            }

            seen[index] = true;

            try
            {
                memberReader.ReadInto(builder, property.Value);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(memberReader.Name);
                throw;
            }
        }

        for (var i = 0; i < memberReaders.Count; i++)
        {
            if (seen[i])
            {
                continue;
            }

            var memberReader = memberReaders[i];
            if (memberReader.IsRequired)
            {
                throw new MissingRequiredMemberException(memberReader.Name);
            }

            memberReader.ReadMissing(builder);
        }

        return build(builder);
    }

    private int IndexOf(JsonProperty property)
    {
        for (var i = 0; i < utf8Names.Length; i++)
        {
            if (property.NameEquals(utf8Names[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

internal sealed class StructureJsonProjectionReader<TBuilder>(
    IReadOnlyList<IJsonMemberReader<TBuilder>> memberReaders
)
{
    private readonly Dictionary<string, IJsonMemberReader<TBuilder>> readersByName =
        memberReaders.ToDictionary(reader => reader.Name, StringComparer.Ordinal);

    public void ReadInto(TBuilder builder, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in value.EnumerateObject())
        {
            if (!readersByName.TryGetValue(member.Name, out var memberReader))
            {
                continue;
            }

            if (member.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (memberReader.IsRequired)
                {
                    throw new MissingRequiredMemberException(member.Name);
                }

                continue;
            }

            seen.Add(member.Name);
            try
            {
                memberReader.ReadInto(builder, member.Value);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(member.Name);
                throw;
            }
        }

        foreach (var memberReader in memberReaders)
        {
            if (seen.Contains(memberReader.Name))
            {
                continue;
            }

            if (memberReader.IsRequired)
            {
                throw new MissingRequiredMemberException(memberReader.Name);
            }

            memberReader.ReadMissing(builder);
        }
    }
}

internal sealed class JsonUnionCaseReaderCompiler<TUnion>(JsonReaderCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IJsonUnionCaseReader<TUnion>> readers = [];

    public IReadOnlyList<IJsonUnionCaseReader<TUnion>> Readers => readers;

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    {
        readers.Add(
            new JsonUnionCaseReader<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema)
            )
        );
    }
}

internal sealed class JsonUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IJsonValueReader<TValue> valueReader
) : IJsonUnionCaseReader<TUnion>
{
    public string Name => WireName(unionCase.Traits, unionCase.Name);

    public TUnion Read(JsonElement value) => unionCase.Create(valueReader.Read(value));
}

internal sealed class JsonOpenUnionCaseReaderCompiler<TUnion>(JsonReaderCompiler compiler)
    : IUnionCaseVisitor<TUnion>
{
    private readonly List<IJsonOpenUnionCaseReader<TUnion>> readers = [];

    public IReadOnlyList<IJsonOpenUnionCaseReader<TUnion>> Readers => readers;

    public IJsonUnknownUnionCaseReader<TUnion>? UnknownReader { get; private set; }

    public void Visit<TValue>(IUnionCaseSchema<TUnion, TValue> unionCase)
    {
        if (IsJsonUnknownCase(unionCase))
        {
            UnknownReader = new JsonUnknownUnionCaseReader<TUnion, TValue>(unionCase);
            return;
        }

        readers.Add(
            new JsonOpenUnionCaseReader<TUnion, TValue>(
                unionCase,
                compiler.CompileValue(unionCase.TargetSchema)
            )
        );
    }
}

internal interface IJsonOpenUnionCaseReader<out TUnion>
{
    string Name { get; }

    TUnion Read(JsonElement value);
}

internal interface IJsonUnknownUnionCaseReader<out TUnion>
{
    TUnion ReadUnknown(JsonElement value);
}

internal sealed class JsonOpenUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase,
    IJsonValueReader<TValue> valueReader
) : IJsonOpenUnionCaseReader<TUnion>
{
    public string Name => WireName(unionCase.Traits, unionCase.Name);

    public TUnion Read(JsonElement value) => unionCase.Create(valueReader.Read(value));
}

internal sealed class JsonUnknownUnionCaseReader<TUnion, TValue>(
    IUnionCaseSchema<TUnion, TValue> unionCase
) : IJsonUnknownUnionCaseReader<TUnion>
{
    public TUnion ReadUnknown(JsonElement value) =>
        unionCase.Create((TValue)(object)Document.FromJsonElement(value));
}

internal sealed class UnionJsonValueReader<T> : IJsonValueReader<T>
{
    private readonly Dictionary<string, IJsonUnionCaseReader<T>> readersByName;

    public UnionJsonValueReader(IReadOnlyList<IJsonUnionCaseReader<T>> readers)
    {
        readersByName = readers.ToDictionary(reader => reader.Name, StringComparer.Ordinal);
    }

    public T Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        var properties = value
            .EnumerateObject()
            .Where(property => !property.NameEquals("__type"))
            .ToArray();
        if (properties.Length != 1)
        {
            throw MalformedRequestException.Serialization(
                $"Expected a union with exactly one member set but found {properties.Length}."
            );
        }

        var property = properties[0];
        if (!readersByName.TryGetValue(property.Name, out var reader))
        {
            throw MalformedRequestException.Serialization(
                $"Unknown union member '{property.Name}'."
            );
        }

        return reader.Read(property.Value);
    }
}

internal sealed class OpenUnionJsonValueReader<T> : IJsonValueReader<T>
{
    private readonly Dictionary<string, IJsonOpenUnionCaseReader<T>> readersByName;
    private readonly IJsonUnknownUnionCaseReader<T>? unknownReader;
    private readonly string discriminatorName;

    public OpenUnionJsonValueReader(
        IReadOnlyList<IJsonOpenUnionCaseReader<T>> readers,
        IJsonUnknownUnionCaseReader<T>? unknownReader,
        string discriminatorName
    )
    {
        readersByName = readers.ToDictionary(reader => reader.Name, StringComparer.Ordinal);
        this.unknownReader = unknownReader;
        this.discriminatorName = discriminatorName;
    }

    public T Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        return discriminatorName.Length == 0
            ? ReadUndiscriminated(value)
            : ReadDiscriminated(value);
    }

    private T ReadUndiscriminated(JsonElement value)
    {
        var properties = value
            .EnumerateObject()
            .Where(property => !property.NameEquals("__type"))
            .ToArray();
        if (properties.Length != 1)
        {
            throw MalformedRequestException.Serialization(
                $"Expected a union with exactly one member set but found {properties.Length}."
            );
        }

        var property = properties[0];
        if (readersByName.TryGetValue(property.Name, out var reader))
        {
            return reader.Read(property.Value);
        }

        return unknownReader is not null
            ? unknownReader.ReadUnknown(value)
            : throw MalformedRequestException.Serialization(
                $"Unknown union member '{property.Name}'."
            );
    }

    private T ReadDiscriminated(JsonElement value)
    {
        if (
            value.TryGetProperty(discriminatorName, out var discriminator)
            && discriminator.ValueKind == JsonValueKind.String
            && readersByName.TryGetValue(discriminator.GetString()!, out var reader)
        )
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    if (!property.NameEquals(discriminatorName))
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            return reader.Read(document.RootElement);
        }

        return unknownReader is not null
            ? unknownReader.ReadUnknown(value)
            : throw new InvalidOperationException(
                $"Discriminated union is missing an unknown JSON case."
            );
    }
}

internal sealed class ListJsonValueReader<TCollection, TElement, TBuilder>(
    IListSchema<TCollection, TElement, TBuilder> schema,
    IJsonValueReader<TElement> elementReader
) : IJsonValueReader<TCollection>
{
    private readonly bool sparse = IsSparse((Schema)schema);

    public TCollection Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Malformed(value, "a JSON array");
        }

        var builder = schema.CreateTypedBuilder();
        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (!sparse)
                {
                    throw MalformedRequestException.Serialization(
                        "A list that is not @sparse cannot contain null."
                    );
                }

                schema.Add(builder, default!);
                index++;
                continue;
            }

            try
            {
                schema.Add(builder, elementReader.Read(element));
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(index.ToString(CultureInfo.InvariantCulture));
                throw;
            }

            index++;
        }

        return schema.Build(builder);
    }
}

internal sealed class MapJsonValueReader<TDictionary, TValue, TBuilder>(
    IMapSchema<TDictionary, TValue, TBuilder> schema,
    IJsonValueReader<TValue> valueReader
) : IJsonValueReader<TDictionary>
{
    private readonly bool sparse = IsSparse((Schema)schema);

    public TDictionary Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Malformed(value, "a JSON object");
        }

        var builder = schema.CreateTypedBuilder();
        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (!sparse)
                {
                    throw MalformedRequestException.Serialization(
                        "A map that is not @sparse cannot contain null."
                    );
                }

                schema.Add(builder, property.Name, default!);
                continue;
            }

            try
            {
                schema.Add(builder, property.Name, valueReader.Read(property.Value));
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(property.Name);
                throw;
            }
        }

        return schema.Build(builder);
    }
}

internal sealed class NullableJsonValueReader<T>(IJsonValueReader<T> inner) : IJsonValueReader<T?>
    where T : struct
{
    public T? Read(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : inner.Read(value);
}

internal sealed class BooleanJsonValueReader : IJsonValueReader<bool>
{
    public bool Read(JsonElement value) =>
        ReadValue(value, "a boolean", static element => element.GetBoolean());
}

internal sealed class ByteJsonValueReader : IJsonValueReader<sbyte>
{
    public sbyte Read(JsonElement value) =>
        ReadValue(value, "a byte", static element => element.GetSByte());
}

internal sealed class ShortJsonValueReader : IJsonValueReader<short>
{
    public short Read(JsonElement value) =>
        ReadValue(value, "a short", static element => element.GetInt16());
}

internal sealed class IntegerJsonValueReader : IJsonValueReader<int>
{
    public int Read(JsonElement value) =>
        ReadValue(value, "an integer", static element => element.GetInt32());
}

internal sealed class LongJsonValueReader : IJsonValueReader<long>
{
    public long Read(JsonElement value) =>
        ReadValue(value, "a long", static element => element.GetInt64());
}

internal sealed class FloatJsonValueReader : IJsonValueReader<float>
{
    public float Read(JsonElement value) => ReadValue(value, "a float", ReadFloat);
}

internal sealed class DoubleJsonValueReader : IJsonValueReader<double>
{
    public double Read(JsonElement value) => ReadValue(value, "a double", ReadDouble);
}

internal sealed class BigIntegerJsonValueReader : IJsonValueReader<BigInteger>
{
    public BigInteger Read(JsonElement value) =>
        ReadValue(
            value,
            "a bigInteger",
            static element => BigInteger.Parse(element.GetRawText(), CultureInfo.InvariantCulture)
        );
}

internal sealed class BigDecimalJsonValueReader : IJsonValueReader<decimal>
{
    public decimal Read(JsonElement value) =>
        ReadValue(value, "a bigDecimal", static element => element.GetDecimal());
}

internal sealed class StringJsonValueReader : IJsonValueReader<string>
{
    public string Read(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null!
            : ReadValue(value, "a string", static element => element.GetString()!);
}

internal sealed class StringEnumJsonValueReader<T>(StringEnumSchema<T> schema) : IJsonValueReader<T>
    where T : IStringEnumValue<T>
{
    // An unmodeled value is not rejected here: enums stay open on the wire, and it is the
    // constraint validator that closes them on the server. Only a non-string is malformed.
    public T Read(JsonElement value) =>
        schema.Create(ReadValue(value, "a string", static element => element.GetString()!));
}

internal sealed class IntEnumJsonValueReader<T>(IntEnumSchema<T> schema) : IJsonValueReader<T>
    where T : struct, Enum
{
    public T Read(JsonElement value) =>
        schema.Create(ReadValue(value, "an integer", static element => element.GetInt32()));
}

internal sealed class BlobJsonValueReader : IJsonValueReader<byte[]>
{
    public byte[] Read(JsonElement value) =>
        ReadValue(value, "a base64-encoded blob", static element => element.GetBytesFromBase64());
}

internal sealed class TimestampJsonValueReader(string format, WireReadMode readMode)
    : IJsonValueReader<DateTimeOffset>
{
    private readonly string expected = $"a {format} timestamp";
    private readonly Func<JsonElement, DateTimeOffset> read = element =>
        TimestampFormat.Read(element, format, readMode);

    public DateTimeOffset Read(JsonElement value) => ReadValue(value, expected, read);
}

internal sealed class DocumentJsonValueReader : IJsonValueReader<Document>
{
    public Document Read(JsonElement value) => Document.FromJsonElement(value);
}

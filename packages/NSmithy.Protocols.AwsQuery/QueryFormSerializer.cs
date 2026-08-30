using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.AwsQuery;

internal enum QueryProtocolKind
{
    AwsQuery,
    Ec2Query,
}

/// <summary>Writes one value's <c>application/x-www-form-urlencoded</c> parameters under a prefix.</summary>
internal interface IQueryFormWriter<in T>
{
    void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value);
}

/// <summary>
/// Serializes an operation input as an AWS Query / EC2 Query form body. The writer tree is compiled
/// once per operation; a request only walks it.
/// </summary>
internal sealed class QueryFormSerializer<T>
{
    private readonly string action;
    private readonly string version;
    private readonly IQueryFormWriter<T> writer;

    public QueryFormSerializer(
        QueryProtocolKind kind,
        string action,
        string version,
        Schema<T> schema
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(schema);
        this.action = action;
        this.version = version;
        writer = new QueryFormWriterCompiler(kind).Compile(schema, memberTraits: null);
    }

    public byte[] Serialize(T value)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("Action", action),
            new("Version", version),
        };
        writer.Write(parameters, string.Empty, value);

        return Encoding.UTF8.GetBytes(
            string.Join(
                "&",
                parameters.Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"
                )
            )
        );
    }
}

internal sealed class QueryFormWriterCompiler(QueryProtocolKind kind)
{
    private readonly QueryProtocolKind kind = kind;
    private static readonly ShapeId Ec2QueryName = ShapeId.Parse("aws.protocols#ec2QueryName");
    private static readonly ShapeId TimestampFormat = ShapeId.Parse("smithy.api#timestampFormat");
    private static readonly ShapeId XmlFlattened = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId XmlName = ShapeId.Parse("smithy.api#xmlName");

    // Structures are the only recursive shape, and the only one whose writer ignores the member's
    // traits, so they alone are memoized.
    private readonly SchemaCompilationCache cache = new();

    public IQueryFormWriter<T> Compile<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits
    ) => (IQueryFormWriter<T>)schema.Resolved.Accept(new Visitor(this, memberTraits));

    private string MemberName(IMemberSchema member)
    {
        if (kind == QueryProtocolKind.AwsQuery)
        {
            return StringTrait(member.MemberTraits, XmlName) ?? member.Name;
        }

        return StringTrait(member.MemberTraits, Ec2QueryName)
            ?? UppercaseFirst(StringTrait(member.MemberTraits, XmlName) ?? member.Name);
    }

    private static string? StringTrait(IReadOnlyDictionary<ShapeId, Trait>? traits, ShapeId id) =>
        traits is not null && traits.TryGetValue(id, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    private static string Join(string first, params string?[] parts)
    {
        var values = parts.Where(part => !string.IsNullOrEmpty(part));
        return first.Length == 0 ? string.Join('.', values) : string.Join('.', [first, .. values]);
    }

    private static string UppercaseFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private sealed class Visitor(
        QueryFormWriterCompiler owner,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    ) : PartialSchemaVisitor<object>
    {
        public override object VisitBoolean(Schema<bool> schema) =>
            new ScalarWriter<bool>(static value => value ? "true" : "false");

        public override object VisitByte(Schema<sbyte> schema) => Number<sbyte>();

        public override object VisitShort(Schema<short> schema) => Number<short>();

        public override object VisitInteger(Schema<int> schema) => Number<int>();

        public override object VisitLong(Schema<long> schema) => Number<long>();

        public override object VisitFloat(Schema<float> schema) =>
            new ScalarWriter<float>(static value =>
                value.ToString("R", CultureInfo.InvariantCulture)
            );

        public override object VisitDouble(Schema<double> schema) =>
            new ScalarWriter<double>(static value =>
                value.ToString("R", CultureInfo.InvariantCulture)
            );

        public override object VisitBigInteger(Schema<BigInteger> schema) => Number<BigInteger>();

        public override object VisitBigDecimal(Schema<decimal> schema) => Number<decimal>();

        public override object VisitString(Schema<string> schema) =>
            new ScalarWriter<string>(static value => value);

        public override object VisitBlob(Schema<byte[]> schema) =>
            new ScalarWriter<byte[]>(Convert.ToBase64String);

        public override object VisitTimestamp(Schema<DateTimeOffset> schema)
        {
            var format =
                StringTrait(traits, TimestampFormat)
                ?? (
                    schema.GetTrait(TimestampFormat) is { HasValue: true } trait
                        ? trait.Value.AsString()
                        : null
                );
            return new ScalarWriter<DateTimeOffset>(
                format switch
                {
                    null or "date-time" => FormatRfc3339,
                    "epoch-seconds" => FormatEpochSeconds,
                    "http-date" => static value =>
                        value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
                    _ => throw new NotSupportedException(
                        $"Timestamp format '{format}' is not supported by AWS Query."
                    ),
                }
            );
        }

        public override object VisitStringEnum<T>(StringEnumSchema<T> schema) =>
            new ScalarWriter<T>(static value => value.Value);

        public override object VisitIntEnum<T>(IntEnumSchema<T> schema) =>
            new ScalarWriter<T>(value =>
                schema.GetIntegerValue(value).ToString(CultureInfo.InvariantCulture)
            );

        public override object VisitNullable<T>(NullableSchema<T> schema) =>
            new NullableWriter<T>((IQueryFormWriter<T>)schema.TypedTarget.Resolved.Accept(this));

        public override object VisitStruct<T, TBuilder>(IStructSchema<T, TBuilder> schema) =>
            owner.cache.GetOrCompile<IQueryFormWriter<T>, DeferredWriter<T>>(
                (Schema)schema,
                static () => new DeferredWriter<T>(),
                _ =>
                {
                    var members = new MemberCompiler<T>(owner);
                    schema.VisitMembers(members);
                    return new StructWriter<T>([.. members.Writers]);
                }
            );

        public override object VisitList<TCollection, TElement, TBuilder>(
            IListSchema<TCollection, TElement, TBuilder> schema
        )
        {
            var flattened =
                owner.kind == QueryProtocolKind.Ec2Query
                || traits?.ContainsKey(XmlFlattened) == true;
            var itemName =
                owner.kind == QueryProtocolKind.AwsQuery && !flattened
                    ? StringTrait(schema.ElementMember.MemberTraits, XmlName) ?? "member"
                    : null;
            return new ListWriter<TCollection, TElement>(
                schema.GetElements,
                owner.Compile(schema.ElementSchema, schema.ElementMember.MemberTraits),
                writeEmptyMarker: owner.kind == QueryProtocolKind.AwsQuery && !flattened,
                itemName
            );
        }

        public override object VisitMap<TDictionary, TValue, TBuilder>(
            IMapSchema<TDictionary, TValue, TBuilder> schema
        )
        {
            if (owner.kind == QueryProtocolKind.Ec2Query)
            {
                return new Ec2MapWriter<TDictionary, TValue>(schema.GetEntries);
            }

            var valueMember = schema.TypedValueMember;
            return new MapWriter<TDictionary, TValue>(
                schema.GetEntries,
                owner.Compile(schema.ValueSchema, valueMember.MemberTraits),
                entryName: traits?.ContainsKey(XmlFlattened) == true ? null : "entry",
                keyName: StringTrait(schema.KeyMember.MemberTraits, XmlName) ?? "key",
                valueName: StringTrait(valueMember.MemberTraits, XmlName) ?? "value"
            );
        }

        public override object VisitUnion<T>(IUnionSchema<T> schema) =>
            new UnsupportedWriter<T>((Schema)schema);

        public override object VisitDocument(Schema<Document> schema) =>
            new UnsupportedWriter<Document>(schema);

        public override object VisitStreamingBlob(Schema<Stream> schema) =>
            new UnsupportedWriter<Stream>(schema);

        public override object VisitEventStream<TEvent>(EventStreamSchema<TEvent> schema) =>
            new UnsupportedWriter<IAsyncEnumerable<TEvent>>(schema);

        private static ScalarWriter<T> Number<T>()
            where T : IFormattable =>
            new(static value => value.ToString(null, CultureInfo.InvariantCulture));

        private static string FormatRfc3339(DateTimeOffset value)
        {
            var utc = value.ToUniversalTime();
            return utc.Ticks % TimeSpan.TicksPerSecond == 0
                ? utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : utc.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
        }

        private static string FormatEpochSeconds(DateTimeOffset value)
        {
            var unixSeconds = value.ToUnixTimeSeconds();
            var fractionalTicks = value.ToUniversalTime().Ticks % TimeSpan.TicksPerSecond;
            if (fractionalTicks == 0)
            {
                return unixSeconds.ToString(CultureInfo.InvariantCulture);
            }

            var fractional = ((decimal)fractionalTicks / TimeSpan.TicksPerSecond).ToString(
                "0.################",
                CultureInfo.InvariantCulture
            );
            return $"{unixSeconds}{fractional[1..]}";
        }
    }

    private sealed class MemberCompiler<T>(QueryFormWriterCompiler owner) : IMemberVisitor<T>
    {
        public List<IQueryFormWriter<T>> Writers { get; } = [];

        public void Visit<TValue>(IMemberSchema<T, TValue> member) =>
            Writers.Add(
                new MemberWriter<T, TValue>(
                    member,
                    owner.MemberName(member),
                    owner.Compile(member.TypedTarget, member.MemberTraits)
                )
            );
    }

    // A scalar at the root has no name to be written under.
    private sealed class ScalarWriter<T>(Func<T, string> format) : IQueryFormWriter<T>
    {
        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value)
        {
            if (value is not null && prefix.Length > 0)
            {
                parameters.Add(new(prefix, format(value)));
            }
        }
    }

    private sealed class NullableWriter<T>(IQueryFormWriter<T> inner) : IQueryFormWriter<T?>
        where T : struct
    {
        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T? value)
        {
            if (value is { } present)
            {
                inner.Write(parameters, prefix, present);
            }
        }
    }

    private sealed class StructWriter<T>(IQueryFormWriter<T>[] members) : IQueryFormWriter<T>
    {
        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value)
        {
            if (value is null)
            {
                return;
            }

            foreach (var member in members)
            {
                member.Write(parameters, prefix, value);
            }
        }
    }

    private sealed class MemberWriter<T, TValue>(
        IMemberSchema<T, TValue> member,
        string name,
        IQueryFormWriter<TValue> target
    ) : IQueryFormWriter<T>
    {
        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value)
        {
            if (member.GetValue(value) is { } memberValue)
            {
                target.Write(parameters, Join(prefix, name), memberValue);
            }
        }
    }

    private sealed class ListWriter<TCollection, TElement>(
        Func<TCollection, IEnumerable<TElement>> elements,
        IQueryFormWriter<TElement> element,
        bool writeEmptyMarker,
        string? itemName
    ) : IQueryFormWriter<TCollection>
    {
        public void Write(
            List<KeyValuePair<string, string>> parameters,
            string prefix,
            TCollection value
        )
        {
            if (value is null)
            {
                return;
            }

            var index = 0;
            foreach (var item in elements(value))
            {
                index++;
                element.Write(
                    parameters,
                    Join(prefix, itemName, index.ToString(CultureInfo.InvariantCulture)),
                    item
                );
            }

            if (index == 0 && writeEmptyMarker)
            {
                parameters.Add(new(prefix, string.Empty));
            }
        }
    }

    private sealed class MapWriter<TDictionary, TValue>(
        Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> entries,
        IQueryFormWriter<TValue> value,
        string? entryName,
        string keyName,
        string valueName
    ) : IQueryFormWriter<TDictionary>
    {
        public void Write(
            List<KeyValuePair<string, string>> parameters,
            string prefix,
            TDictionary map
        )
        {
            if (map is null)
            {
                return;
            }

            var index = 0;
            foreach (var entry in entries(map))
            {
                index++;
                var entryPrefix = Join(
                    prefix,
                    entryName,
                    index.ToString(CultureInfo.InvariantCulture)
                );
                parameters.Add(new(Join(entryPrefix, keyName), entry.Key));
                value.Write(parameters, Join(entryPrefix, valueName), entry.Value);
            }
        }
    }

    // EC2 Query has no map form; an empty map is still nothing to write.
    private sealed class Ec2MapWriter<TDictionary, TValue>(
        Func<TDictionary, IEnumerable<KeyValuePair<string, TValue>>> entries
    ) : IQueryFormWriter<TDictionary>
    {
        public void Write(
            List<KeyValuePair<string, string>> parameters,
            string prefix,
            TDictionary map
        )
        {
            if (map is not null && entries(map).Any())
            {
                throw new NotSupportedException("EC2 Query does not support map input shapes.");
            }
        }
    }

    private sealed class UnsupportedWriter<T>(Schema schema) : IQueryFormWriter<T>
    {
        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value)
        {
            if (value is not null)
            {
                throw new NotSupportedException(
                    $"AWS Query does not support schema kind '{schema.Kind}' on shape '{schema.Id}'."
                );
            }
        }
    }

    private sealed class DeferredWriter<T>
        : IQueryFormWriter<T>,
            IDeferredCompilation<IQueryFormWriter<T>>
    {
        private IQueryFormWriter<T>? compiled;

        public void Complete(IQueryFormWriter<T> writer) => compiled = writer;

        public void Write(List<KeyValuePair<string, string>> parameters, string prefix, T value) =>
            compiled!.Write(parameters, prefix, value);
    }
}

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

internal sealed class QueryFormSerializer(QueryProtocolKind kind)
{
    private static readonly ShapeId Ec2QueryName = ShapeId.Parse("aws.protocols#ec2QueryName");
    private static readonly ShapeId TimestampFormat = ShapeId.Parse("smithy.api#timestampFormat");
    private static readonly ShapeId XmlFlattened = ShapeId.Parse("smithy.api#xmlFlattened");
    private static readonly ShapeId XmlName = ShapeId.Parse("smithy.api#xmlName");

    private readonly List<KeyValuePair<string, string>> parameters = [];

    public byte[] Serialize<T>(string action, string version, Schema<T> schema, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(schema);

        parameters.Clear();
        parameters.Add(new KeyValuePair<string, string>("Action", action));
        parameters.Add(new KeyValuePair<string, string>("Version", version));
        WriteValue(schema, value, string.Empty, traits: null);

        return Encoding.UTF8.GetBytes(
            string.Join(
                "&",
                parameters.Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"
                )
            )
        );
    }

    private void WriteValue(
        Schema schema,
        object? value,
        string prefix,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        if (value is null)
        {
            return;
        }

        schema = UnwrapNullable(schema);
        switch (schema)
        {
            case IStructSchema structure:
                WriteStruct((dynamic)structure, value, prefix);
                return;
            case IListSchema list:
                WriteList(list, value, prefix, traits);
                return;
            case IMapSchema map:
                WriteMap(map, value, prefix, traits);
                return;
            case IUnionSchema:
                throw Unsupported(schema);
        }

        if (prefix.Length == 0)
        {
            return;
        }

        parameters.Add(
            new KeyValuePair<string, string>(prefix, FormatScalar(schema, traits, value))
        );
    }

    private void WriteStruct<T>(IStructSchema<T> schema, object value, string prefix)
    {
        schema.VisitMembers(new StructWriter<T>(this, (T)value, prefix));
    }

    private void WriteList(
        IListSchema schema,
        object value,
        string prefix,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        var elements = schema.GetElementsObject(value).ToArray();
        var flattened = kind == QueryProtocolKind.Ec2Query || HasDirectTrait(traits, XmlFlattened);
        if (elements.Length == 0)
        {
            if (kind == QueryProtocolKind.AwsQuery && !flattened)
            {
                parameters.Add(new KeyValuePair<string, string>(prefix, string.Empty));
            }
            return;
        }

        var itemName =
            kind == QueryProtocolKind.AwsQuery && !flattened
                ? DirectStringTrait(schema.ElementMember.MemberTraits, XmlName) ?? "member"
                : null;
        for (var index = 0; index < elements.Length; index++)
        {
            var itemPrefix = Join(
                prefix,
                itemName,
                (index + 1).ToString(CultureInfo.InvariantCulture)
            );
            WriteValue(
                schema.Element,
                elements[index],
                itemPrefix,
                schema.ElementMember.MemberTraits
            );
        }
    }

    private void WriteMap(
        IMapSchema schema,
        object value,
        string prefix,
        IReadOnlyDictionary<ShapeId, Trait>? traits
    )
    {
        var entries = schema.GetEntriesObject(value).ToArray();
        if (entries.Length == 0)
        {
            return;
        }
        if (kind == QueryProtocolKind.Ec2Query)
        {
            throw new NotSupportedException("EC2 Query does not support map input shapes.");
        }

        var flattened = HasDirectTrait(traits, XmlFlattened);
        var keyName = DirectStringTrait(schema.KeyMember.MemberTraits, XmlName) ?? "key";
        var valueMember = FindMapValueMember(schema);
        var valueName = DirectStringTrait(valueMember.MemberTraits, XmlName) ?? "value";
        for (var index = 0; index < entries.Length; index++)
        {
            var entryPrefix = Join(
                prefix,
                flattened ? null : "entry",
                (index + 1).ToString(CultureInfo.InvariantCulture)
            );
            parameters.Add(
                new KeyValuePair<string, string>(Join(entryPrefix, keyName), entries[index].Key)
            );
            WriteValue(
                schema.Value,
                entries[index].Value,
                Join(entryPrefix, valueName),
                valueMember.MemberTraits
            );
        }
    }

    private static IMemberSchema FindMapValueMember(IMapSchema schema) =>
        (IMemberSchema)((dynamic)schema).TypedValueMember;

    private string MemberName(IMemberSchema member)
    {
        if (kind == QueryProtocolKind.AwsQuery)
        {
            return DirectStringTrait(member.MemberTraits, XmlName) ?? member.Name;
        }

        var ec2Name = DirectStringTrait(member.MemberTraits, Ec2QueryName);
        if (ec2Name is not null)
        {
            return ec2Name;
        }

        return UppercaseFirst(DirectStringTrait(member.MemberTraits, XmlName) ?? member.Name);
    }

    private static string FormatScalar(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        object value
    ) =>
        schema.Kind switch
        {
            ShapeKind.Boolean => (bool)value ? "true" : "false",
            ShapeKind.Byte => ((sbyte)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Short => ((short)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Integer => ((int)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Long => ((long)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.Float => ((float)value).ToString("R", CultureInfo.InvariantCulture),
            ShapeKind.Double => ((double)value).ToString("R", CultureInfo.InvariantCulture),
            ShapeKind.BigInteger => ((BigInteger)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.BigDecimal => ((decimal)value).ToString(CultureInfo.InvariantCulture),
            ShapeKind.String => (string)value,
            ShapeKind.Enum => ((IStringEnumValue)value).Value,
            ShapeKind.IntEnum => ((IIntEnumSchema)schema)
                .GetIntegerValueObject(value)
                .ToString(CultureInfo.InvariantCulture),
            ShapeKind.Blob when value is byte[] bytes => Convert.ToBase64String(bytes),
            ShapeKind.Timestamp => FormatTimestamp(schema, traits, (DateTimeOffset)value),
            _ => throw Unsupported(schema),
        };

    private static string FormatTimestamp(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        DateTimeOffset value
    ) =>
        TraitValue(schema, traits, TimestampFormat) switch
        {
            null or "date-time" => FormatRfc3339(value),
            "epoch-seconds" => FormatEpochSeconds(value),
            "http-date" => value.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture),
            var format => throw new NotSupportedException(
                $"Timestamp format '{format}' is not supported by AWS Query."
            ),
        };

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

    private static string? TraitValue(
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        ShapeId id
    ) =>
        DirectStringTrait(traits, id)
        ?? (schema.GetTrait(id) is { HasValue: true } trait ? trait.Value.AsString() : null);

    private static string? DirectStringTrait(
        IReadOnlyDictionary<ShapeId, Trait>? traits,
        ShapeId id
    ) =>
        traits is not null && traits.TryGetValue(id, out var trait) && trait.HasValue
            ? trait.Value.AsString()
            : null;

    private static bool HasDirectTrait(IReadOnlyDictionary<ShapeId, Trait>? traits, ShapeId id) =>
        traits?.ContainsKey(id) == true;

    private static Schema UnwrapNullable(Schema schema) =>
        schema.Resolved is INullableSchema nullable ? nullable.Target.Resolved : schema.Resolved;

    private static string Join(string first, params string?[] parts)
    {
        var values = parts.Where(part => !string.IsNullOrEmpty(part));
        return first.Length == 0 ? string.Join('.', values) : string.Join('.', [first, .. values]);
    }

    private static string UppercaseFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static NotSupportedException Unsupported(Schema schema) =>
        new($"AWS Query does not support schema kind '{schema.Kind}' on shape '{schema.Id}'.");

    private sealed class StructWriter<T>(QueryFormSerializer owner, T value, string prefix)
        : IMemberVisitor<T>
    {
        public void Visit<TValue>(IMemberSchema<T, TValue> member)
        {
            var memberValue = member.GetValue(value);
            if (memberValue is null)
            {
                return;
            }

            owner.WriteValue(
                member.TargetSchema,
                memberValue,
                Join(prefix, owner.MemberName(member)),
                member.MemberTraits
            );
        }
    }
}

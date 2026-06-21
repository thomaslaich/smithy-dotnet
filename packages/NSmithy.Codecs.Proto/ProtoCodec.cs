using System.Globalization;
using System.Numerics;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Proto;

public interface IProtoCodec<T> : ICodec<T> { }

/// <summary>
/// A schema-driven protobuf (proto3) wire codec. It walks a Smithy <see cref="Schema{T}"/> exactly
/// like <c>CborCodec</c> does, but emits/reads the protobuf binary wire format, driving field
/// numbers from the <c>alloy.proto#protoIndex</c> trait and integer wire types from
/// <c>alloy.proto#protoNumType</c>. This is what lets NSmithy speak gRPC without protoc or
/// Grpc.Tools: the bytes are interoperable with any protobuf implementation that shares the same
/// field numbers.
/// </summary>
/// <remarks>
/// Covers the full unary <c>alloy.proto#grpc</c> surface: structures, the proto3 scalar set (with
/// <c>@protoNumType</c> sint/uint/fixed/sfixed variants), <c>repeated</c> (packed for scalars,
/// length-delimited for messages/strings), dense and <c>@sparse</c> maps (sparse values as
/// <c>google.protobuf.Value</c> so nulls round-trip), intEnum and string enums (proto enum
/// ordinals), Timestamp (<c>google.protobuf.Timestamp</c>), <c>Document</c>
/// (<c>google.protobuf.Value</c>), and unions both as a wrapper message and inlined via
/// <c>@protoInlinedOneOf</c>. Not yet covered: streaming (the codec is per-message; streaming is a
/// transport concern) and <c>@sparse</c> maps whose values are aggregates rather than scalars.
/// </remarks>
public static class ProtoCodec
{
    private static readonly ShapeId ProtoIndexTrait = new("alloy.proto", "protoIndex");
    private static readonly ShapeId ProtoNumTypeTrait = new("alloy.proto", "protoNumType");
    private static readonly ShapeId ProtoInlinedOneOfTrait = new(
        "alloy.proto",
        "protoInlinedOneOf"
    );
    private static readonly ShapeId SparseTrait = new("smithy.api", "sparse");

    // String enums map to proto enums; the declared values (in declaration order) come from the
    // synthetic trait the codegen attaches, matching ProtoGenerator's UNSPECIFIED=0, then 1,2,3…
    private static readonly ShapeId SyntheticEnumTrait = new("smithy.synthetic", "enum");

    public static IProtoCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (Unwrap(schema) is not IStructSchema)
        {
            throw new InvalidOperationException(
                "Protobuf messages must be backed by a structure schema."
            );
        }

        return new ProtoCodecImpl<T>(schema);
    }

    private sealed class ProtoCodecImpl<T>(Schema<T> schema) : IProtoCodec<T>
    {
        public byte[] Serialize(T value)
        {
            if (value is null)
            {
                return [];
            }

            var writer = new ProtoWriter();
            WriteMessage(writer, (IStructSchema)Unwrap(schema), value);
            return writer.ToArray();
        }

        public T Deserialize(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return (T)ReadMessage((IStructSchema)Unwrap(schema), payload)!;
        }
    }

    // ---- writing -----------------------------------------------------------

    private static void WriteMessage(ProtoWriter writer, IStructSchema schema, object value)
    {
        foreach (var member in schema.Members)
        {
            var memberValue = member.GetObject(value);
            if (memberValue is null)
            {
                // proto3 has no null on the wire; absence is expressed by omitting the field.
                continue;
            }

            WriteField(writer, member, memberValue);
        }
    }

    private static void WriteField(ProtoWriter writer, IMemberSchema member, object value)
    {
        var field = ProtoIndex(member.Traits);
        var target = Unwrap(member.Target);

        switch (target.Kind)
        {
            case ShapeKind.List or ShapeKind.Set:
            {
                var list = (IListSchema)target;
                var element = Unwrap(list.Element);
                var elements = list.GetElementsObject(value).ToArray();
                if (IsPackableScalar(element.Kind))
                {
                    var packed = new ProtoWriter();
                    foreach (var item in elements)
                    {
                        WriteValueBody(packed, list.Element, member.Traits, item!);
                    }

                    if (packed.Length > 0)
                    {
                        writer.WriteTag(field, WireType.Len);
                        writer.WriteLengthDelimited(packed.ToArray());
                    }
                }
                else
                {
                    foreach (var item in elements)
                    {
                        WriteTagged(writer, field, list.Element, member.Traits, item!);
                    }
                }

                break;
            }
            case ShapeKind.Map:
            {
                var map = (IMapSchema)target;
                var sparse = target.HasTrait(SparseTrait);
                foreach (var entry in map.GetEntriesObject(value))
                {
                    var sub = new ProtoWriter();
                    WriteTagged(sub, 1, Schemas.String, NoTraits, entry.Key);
                    if (sparse)
                    {
                        // @sparse map values are encoded as google.protobuf.Value so null entries
                        // round-trip (null_value) rather than being dropped.
                        sub.WriteTag(2, WireType.Len);
                        sub.WriteLengthDelimited(EncodeScalarValueMessage(map.Value, entry.Value));
                    }
                    else if (entry.Value is not null)
                    {
                        WriteTagged(sub, 2, map.Value, NoTraits, entry.Value);
                    }

                    writer.WriteTag(field, WireType.Len);
                    writer.WriteLengthDelimited(sub.ToArray());
                }

                break;
            }
            case ShapeKind.Union when target.HasTrait(ProtoInlinedOneOfTrait):
            {
                // @protoInlinedOneOf: the active case is written directly into the parent message at
                // the case's own field number (not wrapped, not at the member's field number).
                var union = (IUnionSchema)target;
                var @case = union.GetCaseObject(value);
                WriteTagged(
                    writer,
                    ProtoIndex(@case.Traits),
                    @case.Target,
                    @case.Traits,
                    @case.GetObject(value)!
                );
                break;
            }
            default:
                WriteTagged(writer, field, member.Target, member.Traits, value);
                break;
        }
    }

    private static void WriteTagged(
        ProtoWriter writer,
        int field,
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        object value
    )
    {
        writer.WriteTag(field, WireTypeOf(Unwrap(schema).Kind, traits));
        WriteValueBody(writer, schema, traits, value);
    }

    private static void WriteValueBody(
        ProtoWriter writer,
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        object value
    )
    {
        var resolved = Unwrap(schema);
        switch (resolved.Kind)
        {
            case ShapeKind.Boolean:
                writer.WriteVarint((bool)value ? 1UL : 0UL);
                break;
            case ShapeKind.Byte:
            case ShapeKind.Short:
            case ShapeKind.Integer:
            case ShapeKind.Long:
                WriteInteger(writer, IntEncodingOf(resolved.Kind, traits), ToInt64(value));
                break;
            case ShapeKind.Float:
                writer.WriteFixed32(BitConverter.SingleToUInt32Bits((float)value));
                break;
            case ShapeKind.Double:
                writer.WriteFixed64(BitConverter.DoubleToUInt64Bits((double)value));
                break;
            case ShapeKind.String:
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes((string)value));
                break;
            case ShapeKind.Blob:
                writer.WriteLengthDelimited((byte[])value);
                break;
            case ShapeKind.BigInteger:
                writer.WriteLengthDelimited(
                    Encoding.UTF8.GetBytes(
                        ((BigInteger)value).ToString(CultureInfo.InvariantCulture)
                    )
                );
                break;
            case ShapeKind.BigDecimal:
                writer.WriteLengthDelimited(
                    Encoding.UTF8.GetBytes(((decimal)value).ToString(CultureInfo.InvariantCulture))
                );
                break;
            case ShapeKind.IntEnum:
                writer.WriteVarint(
                    (ulong)(long)((IIntEnumSchema)resolved).GetIntegerValueObject(value)
                );
                break;
            case ShapeKind.Timestamp:
                writer.WriteLengthDelimited(EncodeTimestamp((DateTimeOffset)value));
                break;
            case ShapeKind.Structure:
            {
                var sub = new ProtoWriter();
                WriteMessage(sub, (IStructSchema)resolved, value);
                writer.WriteLengthDelimited(sub.ToArray());
                break;
            }
            case ShapeKind.Union:
            {
                var sub = new ProtoWriter();
                WriteUnion(sub, (IUnionSchema)resolved, value);
                writer.WriteLengthDelimited(sub.ToArray());
                break;
            }
            case ShapeKind.Enum:
                writer.WriteVarint(
                    (ulong)(long)EnumOrdinal(resolved, ((IStringEnumValue)value).Value)
                );
                break;
            case ShapeKind.Document:
            {
                var sub = new ProtoWriter();
                EncodeDocumentValue(sub, (Document)value);
                writer.WriteLengthDelimited(sub.ToArray());
                break;
            }
            default:
                throw new NotSupportedException(
                    $"Proto codec cannot encode schema kind '{resolved.Kind}'."
                );
        }
    }

    private static void WriteUnion(ProtoWriter writer, IUnionSchema schema, object value)
    {
        var @case = schema.GetCaseObject(value);
        WriteTagged(
            writer,
            ProtoIndex(@case.Traits),
            @case.Target,
            @case.Traits,
            @case.GetObject(value)!
        );
    }

    private static void WriteInteger(ProtoWriter writer, IntEncoding encoding, long value)
    {
        switch (encoding)
        {
            case IntEncoding.VarInt32:
            case IntEncoding.VarInt64:
                writer.WriteVarint((ulong)value);
                break;
            case IntEncoding.SInt32:
                writer.WriteVarint(ProtoWriter.ZigZag((int)value));
                break;
            case IntEncoding.SInt64:
                writer.WriteVarint(ProtoWriter.ZigZag(value));
                break;
            case IntEncoding.UInt32:
                writer.WriteVarint((uint)value);
                break;
            case IntEncoding.UInt64:
                writer.WriteVarint((ulong)value);
                break;
            case IntEncoding.Fixed32:
            case IntEncoding.SFixed32:
                writer.WriteFixed32((uint)(int)value);
                break;
            case IntEncoding.Fixed64:
            case IntEncoding.SFixed64:
                writer.WriteFixed64((ulong)value);
                break;
            default:
                throw new InvalidOperationException($"Unknown integer encoding '{encoding}'.");
        }
    }

    private static byte[] EncodeTimestamp(DateTimeOffset value)
    {
        var ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = ticks / TimeSpan.TicksPerSecond;
        var nanos = (int)(ticks % TimeSpan.TicksPerSecond) * 100;
        var writer = new ProtoWriter();
        if (seconds != 0)
        {
            writer.WriteTag(1, WireType.Varint);
            writer.WriteVarint((ulong)seconds);
        }

        if (nanos != 0)
        {
            writer.WriteTag(2, WireType.Varint);
            writer.WriteVarint((ulong)(long)nanos);
        }

        return writer.ToArray();
    }

    // ---- reading -----------------------------------------------------------

    private static object ReadMessage(IStructSchema schema, ReadOnlySpan<byte> bytes)
    {
        var builder = schema.CreateBuilder();
        var byNumber = BuildFieldMap(schema);
        var inlined = BuildInlinedCaseMap(schema);
        var lists = new Dictionary<int, (IMemberSchema Member, object Builder)>();
        var maps = new Dictionary<int, (IMemberSchema Member, object Builder)>();

        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();

            // @protoInlinedOneOf: a case field number sets the parent union member directly.
            if (inlined.TryGetValue(number, out var ic))
            {
                var caseValue = ReadValueBody(ref reader, ic.Case.Target, ic.Case.Traits, wireType);
                ic.Member.SetObject(builder, ic.Case.CreateObject(caseValue));
                continue;
            }

            if (!byNumber.TryGetValue(number, out var member))
            {
                reader.SkipField(wireType);
                continue;
            }

            var target = Unwrap(member.Target);
            switch (target.Kind)
            {
                case ShapeKind.List or ShapeKind.Set:
                {
                    var list = (IListSchema)target;
                    if (!lists.TryGetValue(number, out var acc))
                    {
                        acc = (member, list.CreateBuilder());
                        lists[number] = acc;
                    }

                    var element = Unwrap(list.Element);
                    if (wireType == WireType.Len && IsPackableScalar(element.Kind))
                    {
                        var packed = new ProtoReader(reader.ReadLengthDelimited());
                        while (!packed.End)
                        {
                            list.AddObject(
                                acc.Builder,
                                ReadValueBody(
                                    ref packed,
                                    list.Element,
                                    member.Traits,
                                    WireTypeOf(element.Kind, member.Traits)
                                )
                            );
                        }
                    }
                    else
                    {
                        list.AddObject(
                            acc.Builder,
                            ReadValueBody(ref reader, list.Element, member.Traits, wireType)
                        );
                    }

                    break;
                }
                case ShapeKind.Map:
                {
                    var map = (IMapSchema)target;
                    if (!maps.TryGetValue(number, out var acc))
                    {
                        acc = (member, map.CreateBuilder());
                        maps[number] = acc;
                    }

                    var entryBytes = reader.ReadLengthDelimited();
                    ReadMapEntry(map, target.HasTrait(SparseTrait), acc.Builder, entryBytes);
                    break;
                }
                default:
                    member.SetObject(
                        builder,
                        ReadValueBody(ref reader, member.Target, member.Traits, wireType)
                    );
                    break;
            }
        }

        foreach (var (_, acc) in lists)
        {
            var list = (IListSchema)Unwrap(acc.Member.Target);
            acc.Member.SetObject(builder, list.BuildObject(acc.Builder));
        }

        foreach (var (_, acc) in maps)
        {
            var map = (IMapSchema)Unwrap(acc.Member.Target);
            acc.Member.SetObject(builder, map.BuildObject(acc.Builder));
        }

        SetMissingCollectionsToEmpty(schema, builder, lists, maps);

        return schema.BuildObject(builder);
    }

    private static void SetMissingCollectionsToEmpty(
        IStructSchema schema,
        object builder,
        Dictionary<int, (IMemberSchema Member, object Builder)> lists,
        Dictionary<int, (IMemberSchema Member, object Builder)> maps
    )
    {
        foreach (var member in schema.Members)
        {
            var target = Unwrap(member.Target);
            switch (target.Kind)
            {
                case ShapeKind.List or ShapeKind.Set:
                {
                    var field = ProtoIndex(member.Traits);
                    if (lists.ContainsKey(field))
                    {
                        break;
                    }

                    var list = (IListSchema)target;
                    member.SetObject(builder, list.BuildObject(list.CreateBuilder()));
                    break;
                }
                case ShapeKind.Map:
                {
                    var field = ProtoIndex(member.Traits);
                    if (maps.ContainsKey(field))
                    {
                        break;
                    }

                    var map = (IMapSchema)target;
                    member.SetObject(builder, map.BuildObject(map.CreateBuilder()));
                    break;
                }
            }
        }
    }

    private static void ReadMapEntry(
        IMapSchema map,
        bool sparse,
        object builder,
        ReadOnlySpan<byte> bytes
    )
    {
        string? key = null;
        object? value = null;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    key = (string)ReadValueBody(ref reader, Schemas.String, NoTraits, wireType)!;
                    break;
                case 2 when sparse:
                    // @sparse value is a google.protobuf.Value message.
                    value = DecodeScalarValueMessage(map.Value, reader.ReadLengthDelimited());
                    break;
                case 2:
                    value = ReadValueBody(ref reader, map.Value, NoTraits, wireType);
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        map.AddObject(builder, key ?? string.Empty, value);
    }

    private static object ReadUnion(IUnionSchema schema, ReadOnlySpan<byte> bytes)
    {
        var byNumber = new Dictionary<int, IUnionCaseSchema>();
        foreach (var @case in schema.Cases)
        {
            byNumber[ProtoIndex(@case.Traits)] = @case;
        }

        var reader = new ProtoReader(bytes);
        object? result = null;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (byNumber.TryGetValue(number, out var @case))
            {
                result = @case.CreateObject(
                    ReadValueBody(ref reader, @case.Target, @case.Traits, wireType)
                );
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return result
            ?? throw new InvalidOperationException("Union message carried no recognized case.");
    }

    private static object? ReadValueBody(
        ref ProtoReader reader,
        Schema schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        WireType wireType
    )
    {
        var resolved = Unwrap(schema);
        switch (resolved.Kind)
        {
            case ShapeKind.Boolean:
                return reader.ReadVarint() != 0;
            case ShapeKind.Byte:
                return (sbyte)ReadInteger(ref reader, IntEncodingOf(resolved.Kind, traits));
            case ShapeKind.Short:
                return (short)ReadInteger(ref reader, IntEncodingOf(resolved.Kind, traits));
            case ShapeKind.Integer:
                return (int)ReadInteger(ref reader, IntEncodingOf(resolved.Kind, traits));
            case ShapeKind.Long:
                return ReadInteger(ref reader, IntEncodingOf(resolved.Kind, traits));
            case ShapeKind.Float:
                return BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
            case ShapeKind.Double:
                return BitConverter.UInt64BitsToDouble(reader.ReadFixed64());
            case ShapeKind.String:
                return Encoding.UTF8.GetString(reader.ReadLengthDelimited());
            case ShapeKind.Blob:
                return reader.ReadLengthDelimited().ToArray();
            case ShapeKind.BigInteger:
                return BigInteger.Parse(
                    Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
                    CultureInfo.InvariantCulture
                );
            case ShapeKind.BigDecimal:
                return decimal.Parse(
                    Encoding.UTF8.GetString(reader.ReadLengthDelimited()),
                    CultureInfo.InvariantCulture
                );
            case ShapeKind.IntEnum:
                return ((IIntEnumSchema)resolved).CreateObject((int)reader.ReadVarint());
            case ShapeKind.Timestamp:
                return DecodeTimestamp(reader.ReadLengthDelimited());
            case ShapeKind.Structure:
                return ReadMessage((IStructSchema)resolved, reader.ReadLengthDelimited());
            case ShapeKind.Union:
                return ReadUnion((IUnionSchema)resolved, reader.ReadLengthDelimited());
            case ShapeKind.Enum:
            {
                var name = EnumValueForOrdinal(resolved, (int)reader.ReadVarint());
                return name is null ? null : ((IStringEnumSchema)resolved).CreateObject(name);
            }
            case ShapeKind.Document:
                return DecodeDocumentValue(reader.ReadLengthDelimited());
            default:
                throw new NotSupportedException(
                    $"Proto codec cannot decode schema kind '{resolved.Kind}' (wire type {wireType})."
                );
        }
    }

    private static long ReadInteger(ref ProtoReader reader, IntEncoding encoding)
    {
        switch (encoding)
        {
            case IntEncoding.VarInt32:
                return (int)(uint)reader.ReadVarint();
            case IntEncoding.VarInt64:
                return (long)reader.ReadVarint();
            case IntEncoding.SInt32:
                return (int)DecodeZigZag(reader.ReadVarint());
            case IntEncoding.SInt64:
                return DecodeZigZag(reader.ReadVarint());
            case IntEncoding.UInt32:
                return (uint)reader.ReadVarint();
            case IntEncoding.UInt64:
                return (long)reader.ReadVarint();
            case IntEncoding.Fixed32:
                return reader.ReadFixed32();
            case IntEncoding.SFixed32:
                return (int)reader.ReadFixed32();
            case IntEncoding.Fixed64:
            case IntEncoding.SFixed64:
                return (long)reader.ReadFixed64();
            default:
                throw new InvalidOperationException($"Unknown integer encoding '{encoding}'.");
        }
    }

    private static DateTimeOffset DecodeTimestamp(ReadOnlySpan<byte> bytes)
    {
        long seconds = 0;
        var nanos = 0;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    seconds = (long)reader.ReadVarint();
                    break;
                case 2:
                    nanos = (int)(long)reader.ReadVarint();
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        var ticks =
            DateTimeOffset.UnixEpoch.UtcTicks + (seconds * TimeSpan.TicksPerSecond) + (nanos / 100);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    // ---- helpers -----------------------------------------------------------

    private static readonly IReadOnlyDictionary<ShapeId, Trait> NoTraits =
        new Dictionary<ShapeId, Trait>();

    private enum IntEncoding
    {
        VarInt32,
        VarInt64,
        SInt32,
        SInt64,
        UInt32,
        UInt64,
        Fixed32,
        Fixed64,
        SFixed32,
        SFixed64,
    }

    private static IntEncoding IntEncodingOf(
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits
    )
    {
        var isLong = kind == ShapeKind.Long;
        var numType =
            traits.TryGetValue(ProtoNumTypeTrait, out var trait)
            && trait.Value.Kind == DocumentKind.String
                ? trait.Value.AsString()
                : null;

        return numType switch
        {
            "SIGNED" => isLong ? IntEncoding.SInt64 : IntEncoding.SInt32,
            "UNSIGNED" => isLong ? IntEncoding.UInt64 : IntEncoding.UInt32,
            "FIXED" => isLong ? IntEncoding.Fixed64 : IntEncoding.Fixed32,
            "FIXED_SIGNED" => isLong ? IntEncoding.SFixed64 : IntEncoding.SFixed32,
            _ => isLong ? IntEncoding.VarInt64 : IntEncoding.VarInt32,
        };
    }

    private static WireType WireTypeOf(
        ShapeKind kind,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) =>
        kind switch
        {
            ShapeKind.Boolean or ShapeKind.IntEnum or ShapeKind.Enum => WireType.Varint,
            ShapeKind.Byte or ShapeKind.Short or ShapeKind.Integer or ShapeKind.Long =>
                IntEncodingOf(kind, traits) switch
                {
                    IntEncoding.Fixed32 or IntEncoding.SFixed32 => WireType.I32,
                    IntEncoding.Fixed64 or IntEncoding.SFixed64 => WireType.I64,
                    _ => WireType.Varint,
                },
            ShapeKind.Float => WireType.I32,
            ShapeKind.Double => WireType.I64,
            ShapeKind.String
            or ShapeKind.Blob
            or ShapeKind.BigInteger
            or ShapeKind.BigDecimal
            or ShapeKind.Timestamp
            or ShapeKind.Structure
            or ShapeKind.Union
            or ShapeKind.Document => WireType.Len,
            _ => throw new NotSupportedException($"No proto wire type for schema kind '{kind}'."),
        };

    private static bool IsPackableScalar(ShapeKind kind) =>
        kind
            is ShapeKind.Boolean
                or ShapeKind.Byte
                or ShapeKind.Short
                or ShapeKind.Integer
                or ShapeKind.Long
                or ShapeKind.Float
                or ShapeKind.Double
                or ShapeKind.IntEnum;

    private static Dictionary<int, IMemberSchema> BuildFieldMap(IStructSchema schema)
    {
        var map = new Dictionary<int, IMemberSchema>();
        foreach (var member in schema.Members)
        {
            // Inlined-oneof members have no field of their own — their union's case field numbers
            // live directly in this message (see BuildInlinedCaseMap).
            if (IsInlinedUnion(Unwrap(member.Target)))
            {
                continue;
            }

            map[ProtoIndex(member.Traits)] = member;
        }

        return map;
    }

    private static Dictionary<
        int,
        (IMemberSchema Member, IUnionCaseSchema Case)
    > BuildInlinedCaseMap(IStructSchema schema)
    {
        var map = new Dictionary<int, (IMemberSchema, IUnionCaseSchema)>();
        foreach (var member in schema.Members)
        {
            if (Unwrap(member.Target) is IUnionSchema union && IsInlinedUnion((Schema)union))
            {
                foreach (var @case in union.Cases)
                {
                    map[ProtoIndex(@case.Traits)] = (member, @case);
                }
            }
        }

        return map;
    }

    private static bool IsInlinedUnion(Schema schema) =>
        schema.Kind == ShapeKind.Union && schema.HasTrait(ProtoInlinedOneOfTrait);

    // ---- string enum ordinals ----------------------------------------------

    private static int EnumOrdinal(Schema enumSchema, string value)
    {
        var members = EnumMembers(enumSchema);
        var index = members.IndexOf(value);
        // Unknown / unmatched maps to the proto UNSPECIFIED = 0 default.
        return index < 0 ? 0 : index + 1;
    }

    private static string? EnumValueForOrdinal(Schema enumSchema, int ordinal)
    {
        if (ordinal <= 0)
        {
            // 0 is the synthetic proto UNSPECIFIED, which has no Smithy enum member.
            return null;
        }

        var members = EnumMembers(enumSchema);
        return ordinal - 1 < members.Count ? members[ordinal - 1] : null;
    }

    private static List<string> EnumMembers(Schema enumSchema)
    {
        var trait = enumSchema.GetTrait(SyntheticEnumTrait);
        if (trait is not { Value.Kind: DocumentKind.Array })
        {
            throw new NotSupportedException(
                "String enum schema is missing the smithy.synthetic#enum trait required to map "
                    + "values to proto enum field numbers."
            );
        }

        var members = new List<string>();
        foreach (var entry in trait.Value.Value.AsArray())
        {
            if (
                entry.Kind == DocumentKind.Object
                && entry.AsObject().TryGetValue("value", out var v)
                && v.Kind == DocumentKind.String
            )
            {
                members.Add(v.AsString());
            }
        }

        return members;
    }

    // ---- google.protobuf.Value (for @sparse map values and Document) -------

    private const int ValueNullField = 1;
    private const int ValueNumberField = 2;
    private const int ValueStringField = 3;
    private const int ValueBoolField = 4;
    private const int ValueStructField = 5;
    private const int ValueListField = 6;

    /// <summary>Encodes a sparse-map value (a declared scalar, or null) as a google.protobuf.Value.</summary>
    private static byte[] EncodeScalarValueMessage(Schema valueSchema, object? value)
    {
        var writer = new ProtoWriter();
        if (value is null)
        {
            writer.WriteTag(ValueNullField, WireType.Varint);
            writer.WriteVarint(0);
            return writer.ToArray();
        }

        switch (Unwrap(valueSchema).Kind)
        {
            case ShapeKind.String:
                writer.WriteTag(ValueStringField, WireType.Len);
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes((string)value));
                break;
            case ShapeKind.Boolean:
                writer.WriteTag(ValueBoolField, WireType.Varint);
                writer.WriteVarint((bool)value ? 1UL : 0UL);
                break;
            case ShapeKind.Byte
            or ShapeKind.Short
            or ShapeKind.Integer
            or ShapeKind.Long
            or ShapeKind.Float
            or ShapeKind.Double:
                writer.WriteTag(ValueNumberField, WireType.I64);
                writer.WriteFixed64(
                    BitConverter.DoubleToUInt64Bits(
                        Convert.ToDouble(value, CultureInfo.InvariantCulture)
                    )
                );
                break;
            default:
                throw new NotSupportedException(
                    $"@sparse map values of kind '{Unwrap(valueSchema).Kind}' are not supported "
                        + "(only scalar values map to google.protobuf.Value)."
                );
        }

        return writer.ToArray();
    }

    /// <summary>Decodes a google.protobuf.Value back to a sparse-map scalar (or null).</summary>
    private static object? DecodeScalarValueMessage(Schema valueSchema, ReadOnlySpan<byte> bytes)
    {
        var kind = Unwrap(valueSchema).Kind;
        object? result = null;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case ValueNullField:
                    reader.ReadVarint();
                    result = null;
                    break;
                case ValueStringField:
                    result = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case ValueBoolField:
                    result = reader.ReadVarint() != 0;
                    break;
                case ValueNumberField:
                    result = CoerceNumber(
                        BitConverter.UInt64BitsToDouble(reader.ReadFixed64()),
                        kind
                    );
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return result;
    }

    private static object CoerceNumber(double number, ShapeKind kind) =>
        kind switch
        {
            // Cast each arm to object so the value boxes as its own type rather than widening to
            // double (the switch expression's common type would otherwise be double).
            ShapeKind.Byte => (object)(sbyte)number,
            ShapeKind.Short => (short)number,
            ShapeKind.Integer => (int)number,
            ShapeKind.Long => (long)number,
            ShapeKind.Float => (float)number,
            _ => number,
        };

    private static void EncodeDocumentValue(ProtoWriter writer, Document document)
    {
        switch (document.Kind)
        {
            case DocumentKind.Null:
                writer.WriteTag(ValueNullField, WireType.Varint);
                writer.WriteVarint(0);
                break;
            case DocumentKind.Boolean:
                writer.WriteTag(ValueBoolField, WireType.Varint);
                writer.WriteVarint(document.AsBoolean() ? 1UL : 0UL);
                break;
            case DocumentKind.Number:
                writer.WriteTag(ValueNumberField, WireType.I64);
                writer.WriteFixed64(BitConverter.DoubleToUInt64Bits((double)document.AsNumber()));
                break;
            case DocumentKind.String:
                writer.WriteTag(ValueStringField, WireType.Len);
                writer.WriteLengthDelimited(Encoding.UTF8.GetBytes(document.AsString()));
                break;
            case DocumentKind.Array:
            {
                var list = new ProtoWriter();
                foreach (var item in document.AsArray())
                {
                    var element = new ProtoWriter();
                    EncodeDocumentValue(element, item);
                    list.WriteTag(1, WireType.Len);
                    list.WriteLengthDelimited(element.ToArray());
                }

                writer.WriteTag(ValueListField, WireType.Len);
                writer.WriteLengthDelimited(list.ToArray());
                break;
            }
            case DocumentKind.Object:
            {
                var structWriter = new ProtoWriter();
                foreach (var (key, item) in document.AsObject())
                {
                    var entry = new ProtoWriter();
                    entry.WriteTag(1, WireType.Len);
                    entry.WriteLengthDelimited(Encoding.UTF8.GetBytes(key));
                    var element = new ProtoWriter();
                    EncodeDocumentValue(element, item);
                    entry.WriteTag(2, WireType.Len);
                    entry.WriteLengthDelimited(element.ToArray());
                    structWriter.WriteTag(1, WireType.Len);
                    structWriter.WriteLengthDelimited(entry.ToArray());
                }

                writer.WriteTag(ValueStructField, WireType.Len);
                writer.WriteLengthDelimited(structWriter.ToArray());
                break;
            }
            default:
                throw new NotSupportedException($"Cannot encode document kind '{document.Kind}'.");
        }
    }

    private static Document DecodeDocumentValue(ReadOnlySpan<byte> bytes)
    {
        var reader = new ProtoReader(bytes);
        var result = Document.Null;
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case ValueNullField:
                    reader.ReadVarint();
                    result = Document.Null;
                    break;
                case ValueNumberField:
                    result = Document.From(
                        (decimal)BitConverter.UInt64BitsToDouble(reader.ReadFixed64())
                    );
                    break;
                case ValueStringField:
                    result = Document.From(Encoding.UTF8.GetString(reader.ReadLengthDelimited()));
                    break;
                case ValueBoolField:
                    result = Document.From(reader.ReadVarint() != 0);
                    break;
                case ValueStructField:
                    result = DecodeDocumentStruct(reader.ReadLengthDelimited());
                    break;
                case ValueListField:
                    result = DecodeDocumentList(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return result;
    }

    private static Document DecodeDocumentStruct(ReadOnlySpan<byte> bytes)
    {
        var fields = new Dictionary<string, Document>(StringComparer.Ordinal);
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == 1)
            {
                var (key, value) = DecodeStructEntry(reader.ReadLengthDelimited());
                fields[key] = value;
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return Document.From(fields);
    }

    private static (string Key, Document Value) DecodeStructEntry(ReadOnlySpan<byte> bytes)
    {
        var key = string.Empty;
        var value = Document.Null;
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            switch (number)
            {
                case 1:
                    key = Encoding.UTF8.GetString(reader.ReadLengthDelimited());
                    break;
                case 2:
                    value = DecodeDocumentValue(reader.ReadLengthDelimited());
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return (key, value);
    }

    private static Document DecodeDocumentList(ReadOnlySpan<byte> bytes)
    {
        var items = new List<Document>();
        var reader = new ProtoReader(bytes);
        while (!reader.End)
        {
            var (number, wireType) = reader.ReadTag();
            if (number == 1)
            {
                items.Add(DecodeDocumentValue(reader.ReadLengthDelimited()));
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return Document.From(items);
    }

    private static int ProtoIndex(IReadOnlyDictionary<ShapeId, Trait> traits)
    {
        if (
            traits.TryGetValue(ProtoIndexTrait, out var trait)
            && trait.Value.Kind == DocumentKind.Number
        )
        {
            return (int)trait.Value.AsNumber();
        }

        throw new InvalidOperationException(
            "Proto codec requires every member to carry an @protoIndex trait."
        );
    }

    private static long ToInt64(object value) =>
        value switch
        {
            sbyte v => v,
            short v => v,
            int v => v,
            long v => v,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
        };

    private static long DecodeZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    private static Schema Unwrap(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}

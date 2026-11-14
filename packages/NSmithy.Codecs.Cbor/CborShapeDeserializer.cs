using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// Reads one CBOR value, described by the plan of the member it belongs to. An aggregate read
/// walks the value and hands each member, element, entry, or case back to the schema. Read
/// <see cref="WireReadMode.Lenient"/>, as a client reads, a required member left out or sent as
/// null is read as its zero value instead of failing.
/// </summary>
internal readonly struct CborShapeDeserializer(
    CborReader reader,
    CborMemberPlan entry,
    WireReadMode mode
) : IShapeDeserializer
{
    public bool TryReadNull()
    {
        if (reader.PeekState() != CborReaderState.Null)
        {
            return false;
        }

        reader.ReadNull();
        return true;
    }

    public bool ReadBoolean() => reader.ReadBoolean();

    public sbyte ReadByte() =>
        Convert.ToSByte(CborWire.ReadInteger(reader), CultureInfo.InvariantCulture);

    public short ReadShort() =>
        Convert.ToInt16(CborWire.ReadInteger(reader), CultureInfo.InvariantCulture);

    public int ReadInteger() =>
        Convert.ToInt32(CborWire.ReadInteger(reader), CultureInfo.InvariantCulture);

    public long ReadLong() =>
        Convert.ToInt64(CborWire.ReadInteger(reader), CultureInfo.InvariantCulture);

    public float ReadFloat() =>
        reader.PeekState() switch
        {
            CborReaderState.SinglePrecisionFloat => reader.ReadSingle(),
            CborReaderState.HalfPrecisionFloat => (float)reader.ReadHalf(),
            _ => Convert.ToSingle(reader.ReadDouble(), CultureInfo.InvariantCulture),
        };

    public double ReadDouble() =>
        reader.PeekState() switch
        {
            CborReaderState.SinglePrecisionFloat => reader.ReadSingle(),
            CborReaderState.HalfPrecisionFloat => (double)reader.ReadHalf(),
            _ => reader.ReadDouble(),
        };

    public BigInteger ReadBigInteger() => CborWire.ReadBigInteger(reader);

    public decimal ReadBigDecimal() => CborWire.ReadBigDecimal(reader);

    public string ReadString() => ReadNullableTextString(reader);

    public byte[] ReadBlob() => ReadNullableByteString(reader);

    public DateTimeOffset ReadTimestamp() => CborWire.ReadTimestamp(reader);

    public Document ReadDocument() =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    public string ReadStringEnum() => reader.ReadTextString();

    public int ReadIntEnum() =>
        Convert.ToInt32(CborWire.ReadInteger(reader), CultureInfo.InvariantCulture);

    public Stream ReadStream() =>
        throw new NotSupportedException("CBOR codec does not support streaming blob schemas.");

    public IAsyncEnumerable<TEvent> ReadEventStream<TEvent>(Schema<TEvent> eventSchema) =>
        throw new NotSupportedException("CBOR codec does not support event stream schemas.");

    public T ReadStruct<T, TBuilder>(StructSchema<T, TBuilder> schema)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return default!;
        }

        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for structure.");
        }

        var builder = schema.CreateTypedBuilder();
        ReadMembers(reader, entry.Shape!, schema, builder, mode);
        schema.CompleteMembers(builder, mode);
        return schema.Build(builder);
    }

    /// <summary>
    /// Reads the members of a structure into <paramref name="builder"/>. An explicit null for a
    /// required member leaves it unset, so it counts as missing; an optional one reads it rather
    /// than taking its default. <paramref name="mode"/> is passed on to nested structures.
    /// </summary>
    internal static void ReadMembers<T, TBuilder>(
        CborReader reader,
        CborShapePlan plan,
        StructSchema<T, TBuilder> schema,
        TBuilder builder,
        WireReadMode mode
    )
    {
        var members = plan.Members;
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var name = reader.ReadTextString();
            var index = plan.IndexOf(name);
            if (index < 0 || !plan.IsIncluded(index))
            {
                reader.SkipValue();
                continue;
            }

            var member = members[index];
            if (reader.PeekState() == CborReaderState.Null && member.IsRequired)
            {
                reader.ReadNull();
                continue;
            }

            var nested = new CborShapeDeserializer(reader, member, mode);
            try
            {
                schema.ReadMember(builder, index, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(member.Name);
                throw;
            }
        }

        reader.ReadEndMap();
    }

    public TCollection ReadList<TCollection, TElement, TBuilder>(
        ListSchema<TCollection, TElement, TBuilder> schema
    )
    {
        if (reader.PeekState() != CborReaderState.StartArray)
        {
            throw new InvalidOperationException("Expected CBOR array for list.");
        }

        var plan = entry.Shape!;
        var element = plan.Members[0];
        var builder = schema.CreateTypedBuilder();
        var index = 0;
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                if (!plan.Sparse)
                {
                    throw new InvalidOperationException(
                        "Non-sparse CBOR list cannot contain null."
                    );
                }

                schema.Add(builder, default!);
                index++;
                continue;
            }

            var nested = new CborShapeDeserializer(reader, element, mode);
            try
            {
                schema.DeserializeElement(builder, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(index.ToString(CultureInfo.InvariantCulture));
                throw;
            }

            index++;
        }

        reader.ReadEndArray();
        return schema.Build(builder);
    }

    public TDictionary ReadMap<TDictionary, TValue, TBuilder>(
        MapSchema<TDictionary, TValue, TBuilder> schema
    )
    {
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected CBOR map for map shape.");
        }

        var plan = entry.Shape!;
        var value = plan.Members[1];
        var builder = schema.CreateTypedBuilder();
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            if (reader.PeekState() == CborReaderState.Null)
            {
                reader.ReadNull();
                if (!plan.Sparse)
                {
                    throw new InvalidOperationException("Non-sparse CBOR map cannot contain null.");
                }

                schema.Add(builder, key, default!);
                continue;
            }

            var nested = new CborShapeDeserializer(reader, value, mode);
            try
            {
                schema.DeserializeEntry(builder, key, ref nested);
            }
            catch (MissingRequiredMemberException exception)
            {
                exception.PrependPathToken(key);
                throw;
            }
        }

        reader.ReadEndMap();
        return schema.Build(builder);
    }

    public T ReadUnion<T>(UnionSchema<T> schema)
    {
        if (reader.PeekState() != CborReaderState.StartMap)
        {
            throw new InvalidOperationException("Expected single-entry CBOR map for union.");
        }

        reader.ReadStartMap();
        if (reader.PeekState() == CborReaderState.EndMap)
        {
            throw new InvalidOperationException("Expected single-entry CBOR map for union.");
        }

        var plan = entry.Shape!;
        var name = reader.ReadTextString();
        var index = plan.IndexOf(name);
        if (index < 0)
        {
            throw new InvalidOperationException($"Unknown union member '{name}'.");
        }

        var nested = new CborShapeDeserializer(reader, plan.Members[index], mode);
        var value = schema.DeserializeCase(index, ref nested);
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            reader.SkipValue();
        }

        reader.ReadEndMap();
        return value;
    }
}

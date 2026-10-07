using System.Formats.Cbor;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Cbor;

/// <summary>
/// Writes CBOR for one aggregate, or for the top-level value. A nested aggregate gets its own
/// serializer positioned on that aggregate's plan.
/// </summary>
internal readonly struct CborShapeSerializer : IShapeSerializer
{
    private readonly CborWriter writer;
    private readonly CborShapePlan? container;
    private readonly CborMemberPlan? root;
    private readonly bool materializeDefaults;

    public CborShapeSerializer(CborWriter writer, CborMemberPlan root, bool materializeDefaults)
    {
        this.writer = writer;
        this.root = root;
        this.materializeDefaults = materializeDefaults;
    }

    private CborShapeSerializer(
        CborWriter writer,
        CborShapePlan container,
        bool materializeDefaults
    )
    {
        this.writer = writer;
        this.container = container;
        this.materializeDefaults = materializeDefaults;
    }

    /// <summary>
    /// Writes the members of <paramref name="value"/> that <paramref name="plan"/> includes, with no
    /// enclosing map, for a caller that adds entries of its own (such as an error's <c>__type</c>).
    /// </summary>
    internal static void WriteMembers<T>(
        CborWriter writer,
        CborShapePlan plan,
        bool materializeDefaults,
        IStructSchema<T> schema,
        T value
    )
    {
        var serializer = new CborShapeSerializer(writer, plan, materializeDefaults);
        schema.SerializeMembers(value, ref serializer);
    }

    private CborMemberPlan Entry(int member) =>
        member == MemberIndex.Root ? root! : container!.Members[member];

    /// <summary>
    /// Positions the writer for a value of <paramref name="member"/>: its key inside a structure,
    /// its single-entry map inside a union. False means the member is not written.
    /// </summary>
    private bool Begin(int member)
    {
        switch (container?.Kind)
        {
            case ShapeKind.Structure:
                if (!container.IsIncluded(member))
                {
                    return false;
                }

                writer.WriteTextString(container.Members[member].Name);
                return true;
            case ShapeKind.Union:
                writer.WriteStartMap(1);
                writer.WriteTextString(container.Members[member].Name);
                return true;
            default:
                return true;
        }
    }

    private void End()
    {
        if (container?.Kind == ShapeKind.Union)
        {
            writer.WriteEndMap();
        }
    }

    public bool WritesDefault(int member) =>
        materializeDefaults
        && container?.Kind == ShapeKind.Structure
        && container.IsIncluded(member)
        && !container.Members[member].IsRequired;

    public void WriteNull(int member)
    {
        if (container?.Kind == ShapeKind.Structure)
        {
            var entry = container.Members[member];
            if (!entry.IsRequired)
            {
                return;
            }
        }

        if (Begin(member))
        {
            writer.WriteNull();
            End();
        }
    }

    public void WriteBoolean(int member, bool value)
    {
        if (Begin(member))
        {
            writer.WriteBoolean(value);
            End();
        }
    }

    public void WriteByte(int member, sbyte value) => WriteInteger(member, value);

    public void WriteShort(int member, short value) => WriteInteger(member, value);

    public void WriteInteger(int member, int value)
    {
        if (Begin(member))
        {
            writer.WriteInt32(value);
            End();
        }
    }

    public void WriteLong(int member, long value)
    {
        if (Begin(member))
        {
            writer.WriteInt64(value);
            End();
        }
    }

    public void WriteFloat(int member, float value)
    {
        if (Begin(member))
        {
            writer.WriteSingle(value);
            End();
        }
    }

    public void WriteDouble(int member, double value)
    {
        if (Begin(member))
        {
            writer.WriteDouble(value);
            End();
        }
    }

    public void WriteBigInteger(int member, BigInteger value)
    {
        if (Begin(member))
        {
            CborWire.WriteBigInteger(writer, value);
            End();
        }
    }

    public void WriteBigDecimal(int member, decimal value)
    {
        if (Begin(member))
        {
            CborWire.WriteBigDecimal(writer, value);
            End();
        }
    }

    public void WriteString(int member, string value)
    {
        if (Begin(member))
        {
            writer.WriteTextString(value);
            End();
        }
    }

    public void WriteBlob(int member, byte[] value)
    {
        if (Begin(member))
        {
            writer.WriteByteString(value);
            End();
        }
    }

    public void WriteTimestamp(int member, DateTimeOffset value)
    {
        if (Begin(member))
        {
            CborWire.WriteTimestamp(writer, value);
            End();
        }
    }

    public void WriteDocument(int member, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported by rpcv2Cbor.");

    public void WriteStringEnum(int member, string value) => WriteString(member, value);

    public void WriteIntEnum(int member, int value) => WriteInteger(member, value);

    // A member a projection excludes is skipped like any other; only one that would be written is
    // a stream this codec cannot encode.
    public void WriteStream(int member, Stream value)
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("CBOR codec does not support streaming blob schemas.");
        }
    }

    public void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    )
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("CBOR codec does not support event stream schemas.");
        }
    }

    private bool IsExcluded(int member) =>
        container?.Kind == ShapeKind.Structure && !container.IsIncluded(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartMap(null);
        // Only the top-level structure follows the codec's default-materialization option;
        // nested structures always write their defaults.
        var nested = new CborShapeSerializer(
            writer,
            entry.Shape!,
            member == MemberIndex.Root ? materializeDefaults : true
        );
        schema.SerializeMembers(value, ref nested);
        writer.WriteEndMap();
        End();
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        // Definite length: the count is known before the elements are written.
        writer.WriteStartArray(schema.GetElements(value).Count());
        var nested = new CborShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeElements(value, ref nested);
        writer.WriteEndArray();
        End();
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        writer.WriteStartMap(schema.GetEntries(value).Count());
        var nested = new CborShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeEntries(value, ref nested);
        writer.WriteEndMap();
        End();
    }

    public void WriteUnion<T>(int member, T value, UnionSchema<T> schema)
    {
        var entry = Entry(member);
        if (!Begin(member))
        {
            return;
        }

        var nested = new CborShapeSerializer(writer, entry.Shape!, true);
        schema.SerializeCase(value, ref nested);
        End();
    }
}

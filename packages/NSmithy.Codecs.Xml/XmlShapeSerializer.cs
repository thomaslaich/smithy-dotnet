using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Xml.XmlWire;

namespace NSmithy.Codecs.Xml;

/// <summary>
/// Writes XML into the element of one aggregate, or into the root element for the top-level value.
/// Each value is written into an element of its own (or an attribute), which is added to the
/// aggregate's element once it is complete.
/// </summary>
internal struct XmlShapeSerializer : IShapeSerializer
{
    private readonly XElement element;
    private readonly XmlShapePlan? container;
    private readonly XmlMemberPlan? root;
    private readonly bool materializeDefaults;

    // A flattened list or map writes its items straight into the enclosing structure's element,
    // each named for the member rather than for the item.
    private readonly string? itemName;

    // The entry element a map's key opened, completed by the value that follows it.
    private XElement? entry;

    public XmlShapeSerializer(XElement root, XmlMemberPlan plan, bool materializeDefaults)
    {
        element = root;
        this.root = plan;
        this.materializeDefaults = materializeDefaults;
    }

    internal XmlShapeSerializer(
        XElement element,
        XmlShapePlan container,
        bool materializeDefaults,
        string? itemName = null
    )
    {
        this.element = element;
        this.container = container;
        this.materializeDefaults = materializeDefaults;
        this.itemName = itemName;
    }

    private readonly XmlMemberPlan Entry(int member) =>
        member == MemberIndex.Root ? root! : container!.Members[member];

    /// <summary>
    /// Creates the element a value of <paramref name="member"/> is written into. Null means the
    /// member is not written as an element: a projection excludes it.
    /// </summary>
    private XElement? Begin(int member)
    {
        if (container is null)
        {
            // A top-level structure's namespace is the document root's, applied by the codec; any
            // other top-level value carries its own.
            if (root!.Shape?.Kind != ShapeKind.Structure)
            {
                ApplyNamespace(element, root.Namespace);
            }

            return element;
        }

        if (container.Kind == ShapeKind.Structure && !container.IsIncluded(member))
        {
            return null;
        }

        var plan = container.Members[member];
        if (container.Kind == ShapeKind.Map)
        {
            // Member 0 is the key, which opened the entry; this is its value.
            var valueElement = new XElement(ChildElementName(entry!, plan.ElementName));
            ApplyNamespace(valueElement, plan.Namespace);
            return valueElement;
        }

        var name = itemName ?? plan.ElementName;
        var child = new XElement(ChildElementName(element, name));
        ApplyNamespace(child, plan.Namespace);
        return child;
    }

    /// <summary>Adds a completed value element to the aggregate's element.</summary>
    private void End(XElement written)
    {
        if (container is null)
        {
            return;
        }

        if (container.Kind == ShapeKind.Map)
        {
            entry!.Add(written);
            element.Add(entry);
            entry = null;
            return;
        }

        element.Add(written);
    }

    private void WriteText(int member, string text)
    {
        if (container?.Kind == ShapeKind.Structure)
        {
            var plan = container.Members[member];
            if (plan.IsAttribute)
            {
                if (container.IsIncluded(member))
                {
                    element.SetAttributeValue(AttributeName(element, plan.ElementName), text);
                }

                return;
            }
        }

        if (Begin(member) is { } target)
        {
            target.Value = text;
            End(target);
        }
    }

    public readonly bool WritesDefault(int member) =>
        materializeDefaults
        && container?.Kind == ShapeKind.Structure
        && container.IsIncluded(member)
        && !container.Members[member].IsRequired;

    public void WriteNull(int member)
    {
        if (container?.Kind == ShapeKind.Structure)
        {
            var plan = container.Members[member];
            if (!plan.IsRequired)
            {
                return;
            }
        }

        // A required member, sparse element, or entry value that is null is an empty element.
        if (container is not null && Begin(member) is { } target)
        {
            End(target);
        }
    }

    public void WriteBoolean(int member, bool value) => WriteText(member, value ? "true" : "false");

    public void WriteByte(int member, sbyte value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteShort(int member, short value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteInteger(int member, int value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteLong(int member, long value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteFloat(int member, float value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteDouble(int member, double value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigInteger(int member, BigInteger value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteBigDecimal(int member, decimal value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    public void WriteString(int member, string value)
    {
        // A map's key is member 0: it opens the entry the value that follows completes.
        if (container?.Kind == ShapeKind.Map && member == 0)
        {
            entry = new XElement(ChildElementName(element, itemName ?? "entry"));
            var keyElement = new XElement(
                ChildElementName(entry, container.Members[0].ElementName),
                value
            );
            ApplyNamespace(keyElement, container.Members[0].Namespace);
            entry.Add(keyElement);
            return;
        }

        WriteText(member, value);
    }

    public void WriteBlob(int member, byte[] value) =>
        WriteText(member, Convert.ToBase64String(value));

    public void WriteTimestamp(int member, DateTimeOffset value) =>
        WriteText(member, XmlTimestamps.Format(value, Entry(member).TimestampFormat));

    public readonly void WriteDocument(int member, Document value) =>
        throw new NotSupportedException("Smithy Document values are not supported in XML.");

    public void WriteStringEnum(int member, string value) => WriteString(member, value);

    public void WriteIntEnum(int member, int value) =>
        WriteText(member, value.ToString(CultureInfo.InvariantCulture));

    // A member a projection excludes is skipped like any other; only one that would be written is
    // a stream this codec cannot encode.
    public readonly void WriteStream(int member, Stream value)
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("XML codec does not support streaming blob schemas.");
        }
    }

    public readonly void WriteEventStream<TEvent>(
        int member,
        IAsyncEnumerable<TEvent> events,
        Schema<TEvent> eventSchema
    )
    {
        if (!IsExcluded(member))
        {
            throw new NotSupportedException("XML codec does not support event stream schemas.");
        }
    }

    private readonly bool IsExcluded(int member) =>
        container?.Kind == ShapeKind.Structure && !container.IsIncluded(member);

    public void WriteStruct<T>(int member, T value, IStructSchema<T> schema)
    {
        var plan = Entry(member);
        if (Begin(member) is not { } target)
        {
            return;
        }

        // Only the top-level structure follows the codec's default-materialization option;
        // nested structures always write their defaults.
        var nested = new XmlShapeSerializer(
            target,
            plan.Shape!,
            container is null ? materializeDefaults : true
        );
        schema.SerializeMembers(value, ref nested);
        End(target);
    }

    public void WriteList<TCollection, TElement>(
        int member,
        TCollection value,
        IListSchema<TCollection, TElement> schema
    )
    {
        var plan = Entry(member);
        if (container?.Kind == ShapeKind.Structure && plan.IsFlattened)
        {
            if (container.IsIncluded(member))
            {
                var flattened = new XmlShapeSerializer(
                    element,
                    plan.Shape!,
                    true,
                    plan.ElementName
                );
                schema.SerializeElements(value, ref flattened);
            }

            return;
        }

        if (Begin(member) is not { } target)
        {
            return;
        }

        var nested = new XmlShapeSerializer(target, plan.Shape!, true);
        schema.SerializeElements(value, ref nested);
        End(target);
    }

    public void WriteMap<TDictionary, TValue>(
        int member,
        TDictionary value,
        IMapSchema<TDictionary, TValue> schema
    )
    {
        var plan = Entry(member);
        if (container?.Kind == ShapeKind.Structure && plan.IsFlattened)
        {
            if (container.IsIncluded(member))
            {
                var flattened = new XmlShapeSerializer(
                    element,
                    plan.Shape!,
                    true,
                    plan.ElementName
                );
                schema.SerializeEntries(value, ref flattened);
            }

            return;
        }

        if (Begin(member) is not { } target)
        {
            return;
        }

        var nested = new XmlShapeSerializer(target, plan.Shape!, true);
        schema.SerializeEntries(value, ref nested);
        End(target);
    }

    public void WriteUnion<T>(int member, T value, IUnionSchema<T> schema)
    {
        var plan = Entry(member);
        if (Begin(member) is not { } target)
        {
            return;
        }

        var nested = new XmlShapeSerializer(target, plan.Shape!, true);
        schema.SerializeCase(value, ref nested);
        End(target);
    }
}

/// <summary>The text forms of a timestamp under each <c>@timestampFormat</c>.</summary>
internal static class XmlTimestamps
{
    public static string Format(DateTimeOffset value, string format) =>
        format switch
        {
            "epoch-seconds" => (value.ToUnixTimeMilliseconds() / 1000.0).ToString(
                CultureInfo.InvariantCulture
            ),
            "http-date" => value.ToString("r", CultureInfo.InvariantCulture),
            _ => value.UtcDateTime.ToString(
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                CultureInfo.InvariantCulture
            ),
        };

    public static DateTimeOffset Parse(string value, string format) =>
        format switch
        {
            "epoch-seconds" => DateTimeOffset.FromUnixTimeMilliseconds(
                (long)(double.Parse(value, CultureInfo.InvariantCulture) * 1000)
            ),
            "http-date" => DateTimeOffset.ParseExact(
                value,
                "r",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None
            ),
            _ => DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind
            ),
        };
}

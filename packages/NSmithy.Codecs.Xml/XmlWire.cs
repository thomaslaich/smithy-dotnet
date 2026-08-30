using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

internal static class XmlWire
{
    private static readonly ShapeId DefaultTrait = new("smithy.api", "default");

    /// <summary>
    /// Resolves a member's modelled default once, at compile time; see the equivalent on
    /// <c>JsonWire</c> for why only the write path may share the resolved instance.
    /// </summary>
    internal static (bool Present, T? Value) ResolveDefault<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        bool materialize
    ) =>
        materialize && TryCreateDefaultValue(schema, traits, out var value)
            ? (true, value)
            : (false, default);

    internal static bool TryCreateDefaultValue<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits,
        out T? value
    )
    {
        if (CompileDefault(schema, traits) is { } create)
        {
            value = create();
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>The member's <c>@default</c> as a factory, or null when it has none.</summary>
    internal static Func<T>? CompileDefault<T>(
        Schema<T> schema,
        IReadOnlyDictionary<ShapeId, Trait> traits
    ) =>
        DefaultValues.TryCompile(schema, traits, honorClientOptional: true, out var create)
            ? create
            : null;

    // Element lookups match on local name only: AWS restXml responses carry a default
    // xmlns on the root (via @xmlNamespace) that all descendants inherit, whereas the
    // schema's element names are unqualified. Namespace-sensitive XName matching would
    // miss every namespaced child (e.g. an S3 ListBuckets list coming back empty).
    internal static IEnumerable<XElement> ChildElements(XElement parent, string localName) =>
        parent
            .Elements()
            .Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    internal static XElement? ChildElement(XElement parent, string localName) =>
        ChildElements(parent, localName).FirstOrDefault();

    internal static string ListItemName(IListSchema schema) =>
        XmlTraits.GetXmlName(schema.ElementMember) ?? schema.Element.MemberName ?? "member";

    internal static string MapKeyName(IMapSchema schema) =>
        XmlTraits.GetXmlName(schema.KeyMember) ?? "key";

    internal static string MapValueName(IMemberSchema valueMember) =>
        XmlTraits.GetXmlName(valueMember) ?? "value";

    internal static string ElementName(IMemberSchema member) =>
        XmlTraits.GetXmlName(member) ?? member.Name;

    internal static XName ChildElementName(XElement parent, string localName)
    {
        var defaultNamespace = parent.GetDefaultNamespace();
        if (
            string.IsNullOrEmpty(defaultNamespace.NamespaceName)
            && !string.IsNullOrEmpty(parent.Name.NamespaceName)
        )
        {
            defaultNamespace = parent.Name.Namespace;
        }
        return string.IsNullOrEmpty(defaultNamespace.NamespaceName)
            ? localName
            : defaultNamespace + localName;
    }

    internal static XName AttributeName(XElement element, string name)
    {
        var separator = name.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
            return name;

        var prefix = name[..separator];
        var localName = name[(separator + 1)..];
        var xmlNamespace =
            element.GetNamespaceOfPrefix(prefix)
            ?? throw new InvalidOperationException(
                $"XML attribute prefix '{prefix}' has no namespace declaration."
            );
        return xmlNamespace + localName;
    }

    internal static void ApplyNamespace(XElement element, XmlNamespace? xmlNamespace)
    {
        if (xmlNamespace is not { } value)
            return;

        var ns = XNamespace.Get(value.Uri);
        if (string.IsNullOrEmpty(value.Prefix))
        {
            var alreadyInNamespace = element.Name.Namespace == ns;
            element.Name = ns + element.Name.LocalName;
            if (!alreadyInNamespace)
            {
                element.SetAttributeValue("xmlns", value.Uri);
            }
        }
        else
        {
            element.SetAttributeValue(XNamespace.Xmlns + value.Prefix, value.Uri);
            if (element.Name.Namespace == ns)
            {
                element.SetAttributeValue("xmlns", value.Uri);
            }
        }
    }

    internal static string RootElementName(Schema schema) =>
        XmlTraits.GetXmlName(schema) ?? schema.Id.Name;
}

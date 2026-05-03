using System.Text;
using System.Xml.Linq;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.RestXml;

public static class RestXmlClientProtocol
{
    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        object? value
    )
    {
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AddHeader(headers, name, value);
    }

    public static void AddPrefixedHeaders(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        object? value
    )
    {
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AddPrefixedHeaders(
            headers,
            prefix,
            value
        );
    }

    public static void AppendQuery(StringBuilder builder, string name, object? value)
    {
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AppendQuery(builder, name, value);
    }

    public static void AppendQueryMap(StringBuilder builder, object? value)
    {
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AppendQueryMap(builder, value);
    }

    public static string EscapeGreedyLabel(object value)
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.EscapeGreedyLabel(value);
    }

    public static string FormatHttpValue(object value)
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.FormatHttpValue(value);
    }

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeBody<T>(codec, content);
    }

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeRequiredBody<T>(
            codec,
            content
        );
    }

    public static string? DeserializeErrorCode(byte[] content)
    {
        var root = GetErrorRoot(content);
        return root.Elements().FirstOrDefault(element => element.Name.LocalName == "Code")?.Value;
    }

    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetHeader<T>(headers, name);
    }

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetRequiredHeader<T>(
            headers,
            name
        );
    }

    public static T GetPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetPrefixedHeaders<T>(
            headers,
            prefix
        );
    }

    public static T GetRequiredPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        return NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetRequiredPrefixedHeaders<T>(
            headers,
            prefix
        );
    }

    private static XElement GetErrorRoot(byte[] content)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(content));
        var root =
            document.Root
            ?? throw new InvalidOperationException(
                "Response body was missing an XML root element."
            );
        return string.Equals(root.Name.LocalName, "ErrorResponse", StringComparison.Ordinal)
            ? root.Elements().FirstOrDefault(element => element.Name.LocalName == "Error") ?? root
            : root;
    }
}

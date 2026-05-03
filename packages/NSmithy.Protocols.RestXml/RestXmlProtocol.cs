using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Xml.Linq;
using NSmithy.Core.Serde;
using NSmithy.Protocols.RestJson;

namespace NSmithy.Protocols.RestXml;

public static class RestXmlProtocol
{
    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        object? value
    )
    {
        RestJsonProtocol.AddHeader(headers, name, value);
    }

    public static void AddPrefixedHeaders(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        object? value
    )
    {
        RestJsonProtocol.AddPrefixedHeaders(headers, prefix, value);
    }

    public static void AppendQuery(StringBuilder builder, string name, object? value)
    {
        RestJsonProtocol.AppendQuery(builder, name, value);
    }

    public static void AppendQueryMap(StringBuilder builder, object? value)
    {
        RestJsonProtocol.AppendQueryMap(builder, value);
    }

    public static string EscapeGreedyLabel(object value)
    {
        return RestJsonProtocol.EscapeGreedyLabel(value);
    }

    public static string FormatHttpValue(object value)
    {
        return RestJsonProtocol.FormatHttpValue(value);
    }

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return RestJsonProtocol.DeserializeBody<T>(codec, content);
    }

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T>
    {
        return RestJsonProtocol.DeserializeRequiredBody<T>(codec, content);
    }

    public static string? DeserializeErrorCode(byte[] content)
    {
        var root = GetErrorRoot(content);
        return root.Elements().FirstOrDefault(element => element.Name.LocalName == "Code")?.Value;
    }

    [return: MaybeNull]
    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return RestJsonProtocol.GetHeader<T>(headers, name);
    }

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        return RestJsonProtocol.GetRequiredHeader<T>(headers, name);
    }

    [return: MaybeNull]
    public static T GetPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        return RestJsonProtocol.GetPrefixedHeaders<T>(headers, prefix);
    }

    public static T GetRequiredPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    )
    {
        return RestJsonProtocol.GetRequiredPrefixedHeaders<T>(headers, prefix);
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

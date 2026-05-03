using NSmithy.Core.Serde;

namespace NSmithy.Client.RestXml;

public static class RestXmlClientProtocol
{
    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        object? value
    ) => NSmithy.Protocols.RestXml.RestXmlClientProtocol.AddHeader(headers, name, value);

    public static void AddPrefixedHeaders(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        object? value
    ) => NSmithy.Protocols.RestXml.RestXmlClientProtocol.AddPrefixedHeaders(headers, prefix, value);

    public static void AppendQuery(System.Text.StringBuilder builder, string name, object? value) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.AppendQuery(builder, name, value);

    public static void AppendQueryMap(System.Text.StringBuilder builder, object? value) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.AppendQueryMap(builder, value);

    public static string EscapeGreedyLabel(object value) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.EscapeGreedyLabel(value);

    public static string FormatHttpValue(object value) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.FormatHttpValue(value);

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.DeserializeBody<T>(codec, content);

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.DeserializeRequiredBody<T>(codec, content);

    public static string? DeserializeErrorCode(byte[] content) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.DeserializeErrorCode(content);

    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    ) => NSmithy.Protocols.RestXml.RestXmlClientProtocol.GetHeader<T>(headers, name);

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    ) => NSmithy.Protocols.RestXml.RestXmlClientProtocol.GetRequiredHeader<T>(headers, name);

    public static T GetPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    ) => NSmithy.Protocols.RestXml.RestXmlClientProtocol.GetPrefixedHeaders<T>(headers, prefix);

    public static T GetRequiredPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    ) =>
        NSmithy.Protocols.RestXml.RestXmlClientProtocol.GetRequiredPrefixedHeaders<T>(
            headers,
            prefix
        );
}

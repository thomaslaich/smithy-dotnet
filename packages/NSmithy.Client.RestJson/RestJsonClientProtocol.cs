using NSmithy.Core.Serde;

namespace NSmithy.Client.RestJson;

public static class RestJsonClientProtocol
{
    public static void AddHeader(
        IDictionary<string, IReadOnlyList<string>> headers,
        string name,
        object? value
    ) => NSmithy.Protocols.RestJson.RestJsonClientProtocol.AddHeader(headers, name, value);

    public static void AddPrefixedHeaders(
        IDictionary<string, IReadOnlyList<string>> headers,
        string prefix,
        object? value
    ) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AddPrefixedHeaders(
            headers,
            prefix,
            value
        );

    public static void AppendQuery(System.Text.StringBuilder builder, string name, object? value) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AppendQuery(builder, name, value);

    public static void AppendQueryMap(System.Text.StringBuilder builder, object? value) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.AppendQueryMap(builder, value);

    public static string EscapeGreedyLabel(object value) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.EscapeGreedyLabel(value);

    public static T DeserializeBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeBody<T>(codec, content);

    public static T DeserializeBody<T>(
        ISmithyCodec codec,
        byte[] content,
        Func<IShapeDeserializer, T> read
    ) => NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeBody(codec, content, read);

    public static T DeserializeRequiredBody<T>(ISmithyCodec codec, byte[] content)
        where T : IDeserializableShape<T> =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeRequiredBody<T>(
            codec,
            content
        );

    public static T DeserializeRequiredBody<T>(
        ISmithyCodec codec,
        byte[] content,
        Func<IShapeDeserializer, T> read
    ) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.DeserializeRequiredBody(
            codec,
            content,
            read
        );

    public static T GetHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    ) => NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetHeader<T>(headers, name);

    public static T GetRequiredHeader<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    ) => NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetRequiredHeader<T>(headers, name);

    public static T GetPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    ) => NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetPrefixedHeaders<T>(headers, prefix);

    public static T GetRequiredPrefixedHeaders<T>(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string prefix
    ) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.GetRequiredPrefixedHeaders<T>(
            headers,
            prefix
        );

    public static string FormatHttpValue(object value) =>
        NSmithy.Protocols.RestJson.RestJsonClientProtocol.FormatHttpValue(value);
}

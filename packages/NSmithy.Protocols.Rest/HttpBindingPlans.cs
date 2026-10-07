using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>Where a structure member travels in an HTTP message.</summary>
internal enum HttpBinding
{
    /// <summary>In the structured body, written by the body codec rather than the binding.</summary>
    Body,
    Label,
    Query,
    QueryParams,
    Header,
    PrefixHeaders,
    StatusCode,
    Payload,
}

/// <summary>Where a request header lands: the transport owns the content headers.</summary>
internal enum HeaderSlot
{
    Headers,
    ContentType,
    ContentHeaders,
}

/// <summary>How an <c>@httpPayload</c> member becomes the message body.</summary>
internal enum PayloadKind
{
    /// <summary>
    /// Encoded by the body codec: structures, unions, documents, alloy's lists and maps, and some
    /// strings.
    /// </summary>
    Codec,
    Blob,
    StreamingBlob,

    /// <summary>A string or enum sent as its raw UTF-8 text.</summary>
    Text,
    EventStream,
}

/// <summary>
/// How the scalars of an HTTP-bound value are written as text: the shape they target, and the
/// traits that change their text form. A list's elements and a map's values are the scalars here.
/// </summary>
internal sealed record HttpTextFormat(ShapeKind Kind, string TimestampFormat, bool Base64Strings)
{
    /// <summary>Whether a header list quotes its elements, which only string-like elements need.</summary>
    public bool QuoteHeaderElements => Kind is ShapeKind.String or ShapeKind.Enum;

    /// <summary>
    /// The format of the scalars <paramref name="target"/> holds. The member's traits apply to each
    /// element of a list: a header list of timestamps is a list of http-dates, a <c>@mediaType</c>
    /// string list a list of base64 strings.
    /// </summary>
    public static HttpTextFormat For(
        Schema target,
        IReadOnlyDictionary<ShapeId, Trait>? memberTraits
    )
    {
        var scalar = HttpBindingPlans.UnwrapNullable(target);
        if (scalar is IListSchema list)
        {
            scalar = HttpBindingPlans.UnwrapNullable(list.Element);
        }

        var timestampFormat =
            memberTraits?.TryGetValue(RestTraits.TimestampFormat, out var memberFormat) == true
                ? memberFormat.Value.AsString()
            : scalar.GetTrait(RestTraits.TimestampFormat) is { } shapeFormat
                ? shapeFormat.Value.AsString()
            : memberTraits?.ContainsKey(RestTraits.HttpHeader) == true ? "http-date"
            : "date-time";
        var base64Strings =
            scalar.Kind == ShapeKind.String
            && (
                memberTraits?.ContainsKey(RestTraits.MediaType) == true
                || scalar.HasTrait(RestTraits.MediaType)
            );
        return new HttpTextFormat(scalar.Kind, timestampFormat, base64Strings);
    }
}

/// <summary>
/// One structure member's HTTP binding, compiled once. A binding's serializer and deserializer
/// look the plan up by the member's index.
/// </summary>
internal sealed class HttpMemberPlan
{
    // A payload's codec, compiled on first use: the payload's value type is only in scope once a
    // value is written or read.
    private object? codec;
    private object? caseName;

    public required int Index { get; init; }

    public required HttpBinding Binding { get; init; }

    public required MemberSchema Member { get; init; }

    /// <summary>The member's target with any nullable wrapper removed.</summary>
    public required Schema Target { get; init; }

    public string MemberName => Member.Name;

    /// <summary>The header or query parameter name, or the <c>@httpPrefixHeaders</c> prefix.</summary>
    public string Name { get; init; } = "";

    public bool IsRequired => Member.IsRequired;

    public bool IsList => Target is IListSchema;

    public HttpTextFormat Format { get; init; } = null!;

    /// <summary>The URI template placeholder a label replaces.</summary>
    public string Placeholder { get; init; } = "";

    public bool Greedy { get; init; }

    public HeaderSlot Slot { get; init; }

    public PayloadKind PayloadKind { get; init; }

    public string? ContentType { get; init; }

    public bool RequiresLength { get; init; }

    /// <summary>The modeled <c>@default</c> of a payload, which an empty body reads as.</summary>
    public Document? Default { get; init; }

    /// <summary>Whether a null structure payload is still sent, as the empty structure.</summary>
    public bool EmptyStructOnNull { get; init; }

    public IRestBodyCodecFactory CodecFactory { get; init; } = null!;

    public ICodec<T> CodecFor<T>(Schema<T> schema) =>
        (ICodec<T>)(codec ??= CodecFactory.FromMember(schema, Member.MemberTraits));

    public ICodec<TEvent> EventCodecFor<TEvent>(Schema<TEvent> eventSchema) =>
        (ICodec<TEvent>)(codec ??= CodecFactory.FromSchema(eventSchema));

    /// <summary>Names the union case an event holds, which frames it on the event stream.</summary>
    public Func<TEvent, string> EventTypeOf<TEvent>(Schema<TEvent> eventSchema) =>
        (Func<TEvent, string>)(
            caseName ??= Schemas.CompileCaseName(
                eventSchema.Resolved as UnionSchema<TEvent>
                    ?? throw new InvalidOperationException(
                        "REST event stream payloads must target a union schema."
                    )
            )
        );

    /// <summary>The schema a string or enum payload is encoded with: as a string either way.</summary>
    public Schema<string> StringSchema => Target as Schema<string> ?? Schemas.String;

    public Schema<Document> DocumentSchema => Target as Schema<Document> ?? Schemas.Document;

    /// <summary>A payload equal to its modeled default is not written, as a body codec would not write it.</summary>
    public bool IsDefault(string value) =>
        Default is { Kind: DocumentKind.String } text && text.AsString() == value;

    public bool IsDefault(Document value) => Default is { } document && document.Equals(value);
}

/// <summary>
/// The parts of an HTTP message a binding's serializer collects. The protocol assembles them,
/// since the order they go on the wire is not the members' order.
/// </summary>
internal sealed class HttpMessageParts(HttpUriBuilder? uri)
{
    // Most messages bind few of these, and many bind none, so each list is created by its first
    // entry: a response that is only a status and a body allocates no list at all.
    private List<string>? texts;
    private List<KeyValuePair<string, List<string>>>? entries;

    public HttpUriBuilder? Uri { get; } = uri;

    public List<KeyValuePair<string, string>>? Query { get; private set; }

    public List<KeyValuePair<string, string>>? QueryParams { get; private set; }

    public List<KeyValuePair<HttpMemberPlan, string>>? Headers { get; private set; }

    public List<KeyValuePair<string, string>>? PrefixHeaders { get; private set; }

    public int? StatusCode { get; set; }

    public RestBody Payload { get; set; } = RestBody.None;

    // Scratch space for the text forms of the member being written.
    internal List<string> Texts => texts ??= [];

    internal List<KeyValuePair<string, List<string>>> Entries => entries ??= [];

    public void AddQuery(string name, string value) => (Query ??= []).Add(new(name, value));

    public void AddQueryParam(string name, string value) =>
        (QueryParams ??= []).Add(new(name, value));

    public void AddHeader(HttpMemberPlan header, string value) =>
        (Headers ??= []).Add(new(header, value));

    public void AddPrefixHeader(string name, string value) =>
        (PrefixHeaders ??= []).Add(new(name, value));
}

internal static class HttpBindingPlans
{
    internal static Schema UnwrapNullable(Schema schema)
    {
        var resolved = schema.Resolved;
        return resolved is INullableSchema nullable ? nullable.Target.Resolved : resolved;
    }
}

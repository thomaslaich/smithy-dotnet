using System.Net.Http;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

public readonly record struct QueryMemberBinding(IMemberSchema Member, string QueryName);

public readonly record struct HeaderMemberBinding(IMemberSchema Member, string HeaderName);

public readonly record struct PrefixHeaderMemberBinding(IMemberSchema Member, string Prefix);

/// <summary>
/// Everything <see cref="RestProtocol"/> needs to (de)serialize one operation, derived once from
/// the operation's <c>@http</c> trait. The body/payload codecs and payload (de)serialize delegates
/// are <em>compiled here</em> and reused for every call — nothing is recompiled per request.
/// </summary>
public sealed class RestOperationBinding<TInput, TOutput>
{
    public required HttpMethod HttpMethod { get; init; }
    public required string UriTemplate { get; init; }

    /// <summary>Default success status from the operation's <c>@http(code)</c> (200 when absent).</summary>
    public required int SuccessStatusCode { get; init; }

    /// <summary>True when the operation has no modeled output (<c>Unit</c>); such responses carry no body.</summary>
    public required bool OutputIsUnit { get; init; }

    /// <summary>Content-Type emitted for a structured (projection) body.</summary>
    public required string BodyContentType { get; init; }

    /// <summary>Pre-resolved value for the request's <c>Accept</c> header.</summary>
    public required string AcceptType { get; init; }

    // Input
    public required IStructSchema<TInput> InputSchema { get; init; }
    public required IReadOnlyList<IMemberSchema> LabelMembers { get; init; }
    public required IReadOnlyList<QueryMemberBinding> QueryMembers { get; init; }
    public IMemberSchema? QueryParamsMember { get; init; }
    public required IReadOnlyList<HeaderMemberBinding> RequestHeaderMembers { get; init; }
    public PrefixHeaderMemberBinding? RequestPrefixHeadersMember { get; init; }
    public required HashSet<string> BoundQueryNames { get; init; }

    /// <summary>Compiled codec for the request body projection (null when there is no body / a payload).</summary>
    public IProjectionCodec<TInput>? InputBodyCodec { get; init; }

    /// <summary>Writes the <c>@httpPayload</c> input to a body (null when there is no payload member).</summary>
    public Func<TInput, RestBody>? InputPayloadWriter { get; init; }

    /// <summary>Reads the <c>@httpPayload</c> input from a body into the builder.</summary>
    public Action<byte[]?, object>? InputPayloadReader { get; init; }

    // Output
    public required IStructSchema<TOutput> OutputSchema { get; init; }
    public IMemberSchema? ResponseCodeMember { get; init; }
    public required IReadOnlyList<HeaderMemberBinding> ResponseHeaderMembers { get; init; }
    public PrefixHeaderMemberBinding? ResponsePrefixHeadersMember { get; init; }

    /// <summary>Compiled codec for the response body projection (null for unit / payload outputs).</summary>
    public IProjectionCodec<TOutput>? OutputBodyCodec { get; init; }

    /// <summary>Writes the <c>@httpPayload</c> output to a body (null when there is no payload member).</summary>
    public Func<TOutput, RestBody>? OutputPayloadWriter { get; init; }

    /// <summary>Reads the <c>@httpPayload</c> output from a body into the builder.</summary>
    public Action<byte[]?, object>? OutputPayloadReader { get; init; }

    internal static RestOperationBinding<TInput, TOutput> CreateFrom(
        OperationSchema<TInput, TOutput> operation,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(codecFactory);

        var httpTrait =
            operation.GetTrait(RestTraits.Http)
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' is missing the @http trait."
            );
        var http = httpTrait.Value.AsObject();
        var httpMethod = ResolveHttpMethod(http["method"].AsString());
        var uriTemplate = http["uri"].AsString();
        var successStatusCode = http.TryGetValue("code", out var codeDoc)
            ? (int)codeDoc.AsNumber()
            : 200;
        var outputIsUnit =
            typeof(TOutput) == typeof(SmithyUnit) || operation.Output.Resolved is UnitSchema;

        var inputSchema =
            operation.Input.Resolved as IStructSchema<TInput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' input must be a structure schema."
            );
        var outputSchema =
            operation.Output.Resolved as IStructSchema<TOutput>
            ?? throw new InvalidOperationException(
                $"Operation '{operation.Id}' output must be a structure schema."
            );

        // Partition input members in a single pass
        var labelMembers = new List<IMemberSchema>();
        var queryMembers = new List<QueryMemberBinding>();
        IMemberSchema? queryParamsMember = null;
        var requestHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? requestPrefixHeadersMember = null;
        IMemberSchema<TInput>? inputPayloadMember = null;
        var inputBodyMembers = new List<IMemberSchema<TInput>>();
        var boundQueryNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in inputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpLabel))
            {
                labelMembers.Add(member);
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpQuery, out var queryTrait))
            {
                var name = queryTrait.Value.AsString();
                queryMembers.Add(new QueryMemberBinding(member, name));
                boundQueryNames.Add(name);
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpQueryParams))
            {
                queryParamsMember = member;
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                requestHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                requestPrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                inputPayloadMember = member;
            }
            else
            {
                inputBodyMembers.Add(member);
            }
        }

        // Request bodies don't materialize top-level defaults (the client sends only what was set).
        IProjectionCodec<TInput>? inputBodyCodec =
            inputPayloadMember is null && inputBodyMembers.Count > 0
                ? codecFactory.CodecFor(
                    Schemas.Project(inputSchema, inputBodyMembers),
                    materializeTopLevelDefaults: false
                )
                : null;

        // Partition output members in a single pass
        IMemberSchema? responseCodeMember = null;
        var responseHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? responsePrefixHeadersMember = null;
        IMemberSchema<TOutput>? outputPayloadMember = null;
        var outputBodyMembers = new List<IMemberSchema<TOutput>>();

        foreach (var member in outputSchema.TypedMembers)
        {
            if (member.Traits.ContainsKey(RestTraits.HttpResponseCode))
            {
                responseCodeMember = member;
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                responseHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (member.Traits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait))
            {
                responsePrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.Traits.ContainsKey(RestTraits.HttpPayload))
            {
                outputPayloadMember = member;
            }
            else
            {
                outputBodyMembers.Add(member);
            }
        }

        // A modeled (non-Unit) structure output always serializes a JSON body — at minimum `{}` —
        // even when every member is bound to a header/status, so build the codec regardless of
        // member count. Unit outputs and payload-bound outputs are handled separately. Responses
        // materialize top-level defaults.
        IProjectionCodec<TOutput>? outputBodyCodec =
            outputPayloadMember is null && !outputIsUnit
                ? codecFactory.CodecFor(
                    Schemas.Project(outputSchema, outputBodyMembers),
                    materializeTopLevelDefaults: true
                )
                : null;

        return new RestOperationBinding<TInput, TOutput>
        {
            HttpMethod = httpMethod,
            UriTemplate = uriTemplate,
            SuccessStatusCode = successStatusCode,
            OutputIsUnit = outputIsUnit,
            BodyContentType = codecFactory.ContentType,
            AcceptType = outputPayloadMember is null
                ? codecFactory.ContentType
                : RestProtocol.PayloadContentType(
                    outputPayloadMember.Target,
                    outputPayloadMember.Traits,
                    codecFactory,
                    rawStringPayloads
                ),
            InputSchema = inputSchema,
            LabelMembers = labelMembers,
            QueryMembers = queryMembers,
            QueryParamsMember = queryParamsMember,
            RequestHeaderMembers = requestHeaderMembers,
            RequestPrefixHeadersMember = requestPrefixHeadersMember,
            BoundQueryNames = boundQueryNames,
            InputBodyCodec = inputBodyCodec,
            InputPayloadWriter = inputPayloadMember is null
                ? null
                : RestProtocol.BuildPayloadWriter(
                    inputPayloadMember,
                    codecFactory,
                    rawStringPayloads,
                    emptyStructOnNull: true
                ),
            InputPayloadReader = inputPayloadMember is null
                ? null
                : RestProtocol.BuildPayloadReader(inputPayloadMember, codecFactory, rawStringPayloads),
            OutputSchema = outputSchema,
            ResponseCodeMember = responseCodeMember,
            ResponseHeaderMembers = responseHeaderMembers,
            ResponsePrefixHeadersMember = responsePrefixHeadersMember,
            OutputBodyCodec = outputBodyCodec,
            OutputPayloadWriter = outputPayloadMember is null
                ? null
                : RestProtocol.BuildPayloadWriter(
                    outputPayloadMember,
                    codecFactory,
                    rawStringPayloads,
                    emptyStructOnNull: false
                ),
            OutputPayloadReader = outputPayloadMember is null
                ? null
                : RestProtocol.BuildPayloadReader(
                    outputPayloadMember,
                    codecFactory,
                    rawStringPayloads
                ),
        };
    }

    private static HttpMethod ResolveHttpMethod(string method) =>
        method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => new HttpMethod(method),
        };
}

public static class RestOperationBinding
{
    /// <summary>
    /// Builds the binding for an operation. Callers build this once per operation (the generated
    /// protocols hold one per operation in a static field), and the body/payload codecs are compiled
    /// here, so the per-call path never recompiles a codec.
    /// </summary>
    public static RestOperationBinding<TInput, TOutput> From<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads
    ) => RestOperationBinding<TInput, TOutput>.CreateFrom(operation, codecFactory, rawStringPayloads);
}

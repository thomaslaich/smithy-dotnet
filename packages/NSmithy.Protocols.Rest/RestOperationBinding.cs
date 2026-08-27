using System.Net.Http;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

public readonly record struct QueryMemberBinding(IStructMemberSchema Member, string QueryName);

public readonly record struct HeaderMemberBinding(IStructMemberSchema Member, string HeaderName);

public readonly record struct PrefixHeaderMemberBinding(IStructMemberSchema Member, string Prefix);

/// <summary>
/// Everything <see cref="RestProtocol"/> needs to (de)serialize one operation, derived once from
/// the operation's <c>@http</c> trait. The body/payload codecs and payload (de)serialize delegates
/// are <em>compiled here</em> and reused for every call — nothing is recompiled per request.
/// </summary>
public sealed class RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>
    where TInputBuilder : notnull
    where TOutputBuilder : notnull
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

    /// <summary>
    /// The <c>Content-Type</c> a request body must carry, or null when the operation models no
    /// request body at all — in which case a request that sends one is rejected rather than ignored.
    /// </summary>
    public required string? RequestContentType { get; init; }

    /// <summary>
    /// True when the request body is an opaque blob payload, whose <c>Content-Type</c> the protocol
    /// therefore does not constrain. See <see cref="RestProtocol.IsOpaquePayload"/>.
    /// </summary>
    public required bool RequestMediaTypeIsOpaque { get; init; }

    /// <summary>The same for the response, which is what an <c>Accept</c> header is matched against.</summary>
    public required bool ResponseMediaTypeIsOpaque { get; init; }

    /// <summary>
    /// Whether a request that sends a body must say what media type it is. AWS's REST protocols
    /// require it and answer an omission with a 415; alloy's <c>simpleRestJson</c> does not, and its
    /// own protocol tests send JSON bodies with no <c>Content-Type</c> at all.
    /// </summary>
    public required bool RequiresDeclaredContentType { get; init; }

    /// <summary>True when the response payload is a streaming blob and must not be buffered.</summary>
    public required bool OutputHasStreamingPayload { get; init; }

    // Input
    public required IStructSchema<TInput, TInputBuilder> InputSchema { get; init; }
    public required IReadOnlyList<IStructMemberSchema> LabelMembers { get; init; }
    public required IReadOnlyList<QueryMemberBinding> QueryMembers { get; init; }
    public IStructMemberSchema? QueryParamsMember { get; init; }
    public required IReadOnlyList<HeaderMemberBinding> RequestHeaderMembers { get; init; }
    public PrefixHeaderMemberBinding? RequestPrefixHeadersMember { get; init; }
    public required HashSet<string> BoundQueryNames { get; init; }

    /// <summary>Compiled codec for the request body projection (null when there is no body / a payload).</summary>
    public IProjectionCodec<TInput, TInputBuilder>? InputBodyCodec { get; init; }

    /// <summary>Writes the <c>@httpPayload</c> input to a body (null when there is no payload member).</summary>
    public Func<TInput, RestBody>? InputPayloadWriter { get; init; }

    /// <summary>Reads the <c>@httpPayload</c> input from a body into the builder.</summary>
    public RestPayloadReader? InputPayloadReader { get; init; }

    // Output
    public required IStructSchema<TOutput, TOutputBuilder> OutputSchema { get; init; }
    public IStructMemberSchema? ResponseCodeMember { get; init; }
    public required IReadOnlyList<HeaderMemberBinding> ResponseHeaderMembers { get; init; }
    public PrefixHeaderMemberBinding? ResponsePrefixHeadersMember { get; init; }

    /// <summary>Compiled codec for the response body projection (null for unit / payload outputs).</summary>
    public IProjectionCodec<TOutput, TOutputBuilder>? OutputBodyCodec { get; init; }

    /// <summary>Writes the <c>@httpPayload</c> output to a body (null when there is no payload member).</summary>
    public Func<TOutput, RestBody>? OutputPayloadWriter { get; init; }

    /// <summary>Reads the <c>@httpPayload</c> output from a body into the builder.</summary>
    public RestPayloadReader? OutputPayloadReader { get; init; }

    internal static RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> CreateFrom(
        OperationSchema<TInput, TOutput> operation,
        IStructSchema<TInput, TInputBuilder> inputSchema,
        IStructSchema<TOutput, TOutputBuilder> outputSchema,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool requiresDeclaredContentType
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(outputSchema);
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

        // A synthetic unit is an input that models no body at all, which is not the same as a
        // structure the model declares with no members: that one still has a body, an empty object.
        var inputIsUnit =
            typeof(TInput) == typeof(SmithyUnit)
            || operation.Input.Resolved is UnitSchema
            || Schemas.IsSyntheticUnit(operation.Input);

        // Partition input members in a single pass
        var labelMembers = new List<IStructMemberSchema>();
        var queryMembers = new List<QueryMemberBinding>();
        IStructMemberSchema? queryParamsMember = null;
        var requestHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? requestPrefixHeadersMember = null;
        IMemberSchema<TInput>? inputPayloadMember = null;
        var inputBodyMembers = new List<IMemberSchema<TInput>>();
        var boundQueryNames = new HashSet<string>(StringComparer.Ordinal);
        var inputMemberCount = 0;

        foreach (var member in Schemas.GetMembers(inputSchema))
        {
            inputMemberCount++;
            if (member.MemberTraits.ContainsKey(RestTraits.HttpLabel))
            {
                labelMembers.Add(member);
            }
            else if (member.MemberTraits.TryGetValue(RestTraits.HttpQuery, out var queryTrait))
            {
                var name = queryTrait.Value.AsString();
                queryMembers.Add(new QueryMemberBinding(member, name));
                boundQueryNames.Add(name);
            }
            else if (member.MemberTraits.ContainsKey(RestTraits.HttpQueryParams))
            {
                queryParamsMember = member;
            }
            else if (member.MemberTraits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                requestHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (
                member.MemberTraits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait)
            )
            {
                requestPrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.MemberTraits.ContainsKey(RestTraits.HttpPayload))
            {
                inputPayloadMember = member;
            }
            else
            {
                inputBodyMembers.Add(member);
            }
        }

        // A structure the model declares still has a body when every member is bound elsewhere —
        // an empty object — but a synthetic unit has none, and an input whose members are all bound
        // to the URI or headers leaves nothing for the body to carry.
        var inputHasBody =
            inputPayloadMember is null
            && !inputIsUnit
            && (inputBodyMembers.Count > 0 || inputMemberCount == 0);

        // Request bodies don't materialize top-level defaults (the client sends only what was set).
        IProjectionCodec<TInput, TInputBuilder>? inputBodyCodec =
            inputPayloadMember is null && inputBodyMembers.Count > 0
                ? codecFactory.FromProjection(
                    Schemas.Project(inputSchema, inputBodyMembers),
                    new CodecFactoryOptions
                    {
                        MaterializeTopLevelDefaults = false,
                        DefaultRootName = operation.Id.Name + "Request",
                    }
                )
                : null;

        // Partition output members in a single pass
        IStructMemberSchema? responseCodeMember = null;
        var responseHeaderMembers = new List<HeaderMemberBinding>();
        PrefixHeaderMemberBinding? responsePrefixHeadersMember = null;
        IMemberSchema<TOutput>? outputPayloadMember = null;
        var outputBodyMembers = new List<IMemberSchema<TOutput>>();

        foreach (var member in Schemas.GetMembers(outputSchema))
        {
            if (member.MemberTraits.ContainsKey(RestTraits.HttpResponseCode))
            {
                responseCodeMember = member;
            }
            else if (member.MemberTraits.TryGetValue(RestTraits.HttpHeader, out var headerTrait))
            {
                responseHeaderMembers.Add(
                    new HeaderMemberBinding(member, headerTrait.Value.AsString())
                );
            }
            else if (
                member.MemberTraits.TryGetValue(RestTraits.HttpPrefixHeaders, out var prefixTrait)
            )
            {
                responsePrefixHeadersMember = new PrefixHeaderMemberBinding(
                    member,
                    prefixTrait.Value.AsString()
                );
            }
            else if (member.MemberTraits.ContainsKey(RestTraits.HttpPayload))
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
        IProjectionCodec<TOutput, TOutputBuilder>? outputBodyCodec =
            outputPayloadMember is null && !outputIsUnit
                ? codecFactory.FromProjection(
                    Schemas.Project(outputSchema, outputBodyMembers),
                    new CodecFactoryOptions
                    {
                        MaterializeTopLevelDefaults = true,
                        DefaultRootName = operation.Id.Name + "Response",
                    }
                )
                : null;

        return new RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>
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
                    outputPayloadMember.MemberTraits,
                    codecFactory,
                    rawStringPayloads
                ),
            RequestContentType =
                inputPayloadMember is not null
                    ? RestProtocol.PayloadContentType(
                        inputPayloadMember.Target,
                        inputPayloadMember.MemberTraits,
                        codecFactory,
                        rawStringPayloads
                    )
                : inputHasBody ? codecFactory.ContentType
                : null,
            RequestMediaTypeIsOpaque =
                inputPayloadMember is not null
                && RestProtocol.IsOpaquePayload(
                    inputPayloadMember.Target,
                    inputPayloadMember.MemberTraits
                ),
            ResponseMediaTypeIsOpaque =
                outputPayloadMember is not null
                && RestProtocol.IsOpaquePayload(
                    outputPayloadMember.Target,
                    outputPayloadMember.MemberTraits
                ),
            RequiresDeclaredContentType = requiresDeclaredContentType,
            OutputHasStreamingPayload =
                outputPayloadMember is not null
                && (
                    outputPayloadMember.Target.Resolved is IEventStreamSchema
                    || outputPayloadMember.MemberTraits.ContainsKey(RestTraits.Streaming)
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
                : RestProtocol.BuildPayloadReader(
                    inputPayloadMember,
                    codecFactory,
                    rawStringPayloads
                ),
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
    public static RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> From<
        TInput,
        TOutput,
        TInputBuilder,
        TOutputBuilder
    >(
        OperationSchema<TInput, TOutput> operation,
        IStructSchema<TInput, TInputBuilder> inputSchema,
        IStructSchema<TOutput, TOutputBuilder> outputSchema,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool requiresDeclaredContentType = true
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull =>
        RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>.CreateFrom(
            operation,
            inputSchema,
            outputSchema,
            codecFactory,
            rawStringPayloads,
            requiresDeclaredContentType
        );

    public static dynamic From<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        bool requiresDeclaredContentType = true
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
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

        return From(
            (dynamic)operation,
            (dynamic)inputSchema,
            (dynamic)outputSchema,
            codecFactory,
            rawStringPayloads,
            requiresDeclaredContentType
        );
    }
}

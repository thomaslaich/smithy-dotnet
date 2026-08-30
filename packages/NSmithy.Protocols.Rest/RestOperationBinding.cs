using System.Net.Http;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// Everything <see cref="RestProtocol"/> needs to (de)serialize one operation, derived once from
/// the operation's <c>@http</c> trait. Every binding plan and body codec is <em>compiled here</em>
/// and reused for every call — nothing is recompiled per request.
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

    internal RestStructBinding<TInput, TInputBuilder> Input { get; init; } = null!;

    internal RestStructBinding<TOutput, TOutputBuilder> Output { get; init; } = null!;

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

        var input = RestStructBinding<TInput, TInputBuilder>.Compile(
            inputSchema,
            HttpBindingSide.Request,
            codecFactory,
            rawStringPayloads,
            emptyStructOnNullPayload: true,
            uriTemplate
        );
        var output = RestStructBinding<TOutput, TOutputBuilder>.Compile(
            outputSchema,
            HttpBindingSide.Response,
            codecFactory,
            rawStringPayloads,
            emptyStructOnNullPayload: false
        );

        // A structure the model declares still has a body when every member is bound elsewhere —
        // an empty object — but a synthetic unit has none, and an input whose members are all bound
        // to the URI or headers leaves nothing for the body to carry.
        var inputHasBody =
            input.PayloadMember is null
            && !inputIsUnit
            && (input.BodyMemberNames.Count > 0 || input.MemberCount == 0);

        // Request bodies don't materialize top-level defaults (the client sends only what was set).
        if (input.PayloadMember is null && input.BodyMemberNames.Count > 0)
        {
            input.BodyCodec = input.CompileBodyCodec(
                codecFactory,
                new CodecFactoryOptions
                {
                    MaterializeTopLevelDefaults = false,
                    DefaultRootName = operation.Id.Name + "Request",
                }
            );
        }

        // A modeled (non-Unit) structure output always serializes a body — at minimum `{}` — even
        // when every member is bound to a header/status, so build the codec regardless of member
        // count. Unit outputs and payload-bound outputs are handled separately. Responses
        // materialize top-level defaults.
        if (output.PayloadMember is null && !outputIsUnit)
        {
            output.BodyCodec = output.CompileBodyCodec(
                codecFactory,
                new CodecFactoryOptions
                {
                    MaterializeTopLevelDefaults = true,
                    DefaultRootName = operation.Id.Name + "Response",
                }
            );
        }

        var inputPayload = input.PayloadMember;
        var outputPayload = output.PayloadMember;

        return new RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder>
        {
            HttpMethod = httpMethod,
            UriTemplate = uriTemplate,
            SuccessStatusCode = successStatusCode,
            OutputIsUnit = outputIsUnit,
            BodyContentType = codecFactory.ContentType,
            AcceptType = outputPayload is null
                ? codecFactory.ContentType
                : RestProtocol.PayloadContentType(
                    outputPayload.Target,
                    outputPayload.MemberTraits,
                    codecFactory,
                    rawStringPayloads
                ),
            RequestContentType =
                inputPayload is not null
                    ? RestProtocol.PayloadContentType(
                        inputPayload.Target,
                        inputPayload.MemberTraits,
                        codecFactory,
                        rawStringPayloads
                    )
                : inputHasBody ? codecFactory.ContentType
                : null,
            RequestMediaTypeIsOpaque =
                inputPayload is not null
                && RestProtocol.IsOpaquePayload(inputPayload.Target, inputPayload.MemberTraits),
            ResponseMediaTypeIsOpaque =
                outputPayload is not null
                && RestProtocol.IsOpaquePayload(outputPayload.Target, outputPayload.MemberTraits),
            RequiresDeclaredContentType = requiresDeclaredContentType,
            OutputHasStreamingPayload =
                outputPayload is not null
                && (
                    outputPayload.Target.Resolved is IEventStreamSchema
                    || outputPayload.MemberTraits.ContainsKey(RestTraits.Streaming)
                ),
            Input = input,
            Output = output,
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

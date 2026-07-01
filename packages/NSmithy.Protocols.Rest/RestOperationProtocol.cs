using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// REST <see cref="IServiceProtocol"/> shared by restJson1 / simpleRestJson / restXml. A protocol is
/// described by three things: its body wire format (<see cref="IRestBodyCodecFactory"/>, JSON vs
/// XML), whether string/enum payloads are raw <c>text/plain</c> (<paramref name="rawStringPayloads"/>
/// — true for restJson1/restXml, false for simpleRestJson), and the modeled-error discriminator
/// header (<paramref name="errorTypeHeader"/>; <c>null</c> for protocols that don't serialize errors
/// via a header, such as restXml). Unlike rpcv2Cbor, REST's per-operation path is authored
/// <c>@http</c> data on the operation schema, so the service schema is not consulted here.
/// </summary>
public sealed class RestServiceProtocol(
    IRestBodyCodecFactory codecFactory,
    Func<SmithyHttpResponse, string?> errorDiscriminator,
    bool rawStringPayloads,
    string? errorTypeHeader
) : IServiceProtocol
{
    public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new RestOperationProtocol<TInput, TOutput>(
            RestOperationBinding.From(operation, codecFactory, rawStringPayloads),
            operation.Errors,
            codecFactory,
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader
        );
    }
}

/// <summary>
/// REST protocol bound to one operation. The <see cref="RestOperationBinding{TInput, TOutput}"/>
/// (parsed from the operation's <c>@http</c> trait, with body/payload codecs already compiled) is
/// built once and reused; all wire logic is delegated to <see cref="RestProtocol"/>.
/// </summary>
public sealed class RestOperationProtocol<TInput, TOutput>(
    RestOperationBinding<TInput, TOutput> binding,
    IReadOnlyList<IOperationErrorSchema> modeledErrors,
    IRestBodyCodecFactory codecFactory,
    Func<SmithyHttpResponse, string?> errorDiscriminator,
    bool rawStringPayloads,
    string? errorTypeHeader
) : IOperationProtocol<TInput, TOutput>
{
    public SmithyHttpRequest SerializeRequest(TInput input) =>
        RestProtocol.SerializeRequest(binding, input);

    public TOutput DeserializeResponse(SmithyHttpResponse response) =>
        RestProtocol.DeserializeResponse(binding, response);

    public TInput DeserializeRequest(SmithyHttpRequest request) =>
        RestProtocol.DeserializeRequest(binding, request);

    public SmithyHttpResponse SerializeResponse(TOutput output) =>
        RestProtocol.SerializeResponse(binding, output);

    public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

    public string? GetErrorDiscriminator(SmithyHttpResponse response) =>
        errorDiscriminator(response);

    public bool RequiresErrorDiscriminator => false;

    public bool SupportsHttpStatusErrorFallback => true;

    public IReadOnlyList<IOperationErrorSchema> ModeledErrors { get; } = modeledErrors;

    public TError DeserializeError<TError>(
        Schema<TError> errorSchema,
        SmithyHttpResponse response
    ) => RestProtocol.DeserializeError(errorSchema, response, codecFactory, rawStringPayloads);

    public SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    ) =>
        errorTypeHeader is null
            ? throw new NotSupportedException(
                "This REST protocol does not support server-side error serialization."
            )
            : RestProtocol.SerializeError(
                errorSchema,
                value,
                errorShapeId,
                statusCode,
                codecFactory,
                rawStringPayloads,
                errorTypeHeader
            );
}

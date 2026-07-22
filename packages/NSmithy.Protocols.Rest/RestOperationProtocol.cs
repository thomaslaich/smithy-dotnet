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
            errorTypeHeader,
            SmithyRequestModifiers.Compile(operation)
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
    string? errorTypeHeader,
    Action<SmithyHttpRequest>? requestTransform = null
) : IOperationProtocol<TInput, TOutput>
{
    private readonly IReadOnlyList<HttpOperationError> httpErrors =
        RestProtocol.CompileErrorDeserializers(modeledErrors, codecFactory, rawStringPayloads);

    // restXml has no error discriminator header and does not serialize modeled errors server-side;
    // an empty matcher leaves such exceptions to propagate (surfaced as a 500 by the host).
    private readonly ModeledErrorSerializer serverErrors = errorTypeHeader is null
        ? ModeledErrorSerializer.Compile([], _ => throw new InvalidOperationException())
        : ModeledErrorSerializer.Compile(
            modeledErrors,
            error =>
                CompileServerError((dynamic)error, codecFactory, rawStringPayloads, errorTypeHeader)
        );

    public SmithyHttpRequest SerializeRequest(TInput input)
    {
        var request = RestProtocol.SerializeRequest(binding, input);
        requestTransform?.Invoke(request);
        return request;
    }

    public TOutput DeserializeResponse(SmithyHttpResponse response) =>
        RestProtocol.DeserializeResponse(binding, response);

    public TInput DeserializeRequest(SmithyHttpRequest request) =>
        RestProtocol.DeserializeRequest(binding, request);

    public SmithyServerResponse SerializeResponse(TOutput output) =>
        SmithyServerResponse.FromHttp(RestProtocol.SerializeResponse(binding, output));

    public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

    // REST errors may carry a discriminator header, but a response without one can still resolve
    // via the HTTP status code.
    public ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpResponse response,
        CancellationToken cancellationToken = default
    ) =>
        ValueTask.FromResult(
            OperationProtocolErrors.DeserializeModeledError(
                httpErrors,
                response,
                errorDiscriminator,
                requiresErrorDiscriminator: false,
                supportsHttpStatusErrorFallback: true
            )
        );

    public bool TrySerializeError(Exception exception, out SmithyServerResponse response) =>
        serverErrors.TrySerialize(exception, out response);

    private static (Type, Func<Exception, SmithyServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
        where TError : Exception =>
        (
            typeof(TError),
            exception =>
                SmithyServerResponse.FromHttp(
                    RestProtocol.SerializeError(
                        error.Schema,
                        (TError)exception,
                        error.Id.ToString(),
                        error.HttpStatusCode,
                        codecFactory,
                        rawStringPayloads,
                        errorTypeHeader
                    )
                )
        );
}

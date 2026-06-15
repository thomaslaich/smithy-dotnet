using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// REST <see cref="IServiceProtocol"/> shared by restJson1 / restXml. The wire encoding differs
/// only by <see cref="IRestBodyFormat"/> (JSON vs XML) and the error-type discriminator, both
/// supplied by the concrete protocol. Unlike rpcv2Cbor, REST's per-operation path is authored
/// <c>@http</c> data on the operation schema, so the service schema is not consulted here.
/// </summary>
public sealed class RestServiceProtocol(
    IRestBodyFormat bodyFormat,
    Func<SmithyHttpResponse, string?> errorDiscriminator
) : IServiceProtocol
{
    public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new RestOperationProtocol<TInput, TOutput>(
            RestOperationBinding.From(operation),
            bodyFormat,
            errorDiscriminator
        );
    }
}

/// <summary>
/// REST protocol bound to one operation. The <see cref="RestOperationBinding{TInput, TOutput}"/>
/// (parsed from the operation's <c>@http</c> trait) is computed once and reused; all wire logic is
/// delegated to the existing <see cref="RestProtocol"/> primitives.
/// </summary>
public sealed class RestOperationProtocol<TInput, TOutput>(
    RestOperationBinding<TInput, TOutput> binding,
    IRestBodyFormat bodyFormat,
    Func<SmithyHttpResponse, string?> errorDiscriminator
) : IOperationProtocol<TInput, TOutput>
{
    public SmithyHttpRequest SerializeRequest(TInput input) =>
        RestProtocol.SerializeRequest(binding, input, bodyFormat);

    public TOutput DeserializeResponse(SmithyHttpResponse response) =>
        RestProtocol.DeserializeResponse(binding, response, bodyFormat);

    public TInput DeserializeRequest(SmithyHttpRequest request) =>
        RestProtocol.DeserializeRequest(binding, request, bodyFormat);

    public SmithyHttpResponse SerializeResponse(TOutput output) =>
        RestProtocol.SerializeResponse(binding, output, bodyFormat);

    public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

    public string? GetErrorDiscriminator(SmithyHttpResponse response) =>
        errorDiscriminator(response);

    public TError DeserializeError<TError>(
        Schema<TError> errorSchema,
        SmithyHttpResponse response
    ) => RestProtocol.DeserializeError(errorSchema, response, bodyFormat);

    public SmithyHttpResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError value,
        string errorShapeId,
        int statusCode
    ) =>
        throw new NotSupportedException(
            "REST server-side error serialization is not yet implemented."
        );
}

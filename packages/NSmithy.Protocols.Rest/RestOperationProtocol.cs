using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
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
    Func<SmithyHttpClientResponse, string?> errorDiscriminator,
    bool rawStringPayloads,
    string? errorTypeHeader
) : IServiceProtocol
{
    public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return CreateOperation(
            operation,
            operation.Errors,
            codecFactory,
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader,
            SmithyRequestModifiers.Compile(operation),
            SmithyValidator.FromSchema(operation.Input)
        );
    }

    private static IOperationProtocol<TInput, TOutput> CreateOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        IReadOnlyList<IOperationErrorSchema> modeledErrors,
        IRestBodyCodecFactory codecFactory,
        Func<SmithyHttpClientResponse, string?> errorDiscriminator,
        bool rawStringPayloads,
        string? errorTypeHeader,
        Action<SmithyHttpRequest>? requestTransform
    )
    {
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

        return CreateOperation(
            operation,
            (dynamic)inputSchema,
            (dynamic)outputSchema,
            modeledErrors,
            codecFactory,
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader,
            requestTransform
        );
    }

    private static RestOperationProtocol<
        TInput,
        TOutput,
        TInputBuilder,
        TOutputBuilder
    > CreateOperation<TInput, TOutput, TInputBuilder, TOutputBuilder>(
        OperationSchema<TInput, TOutput> operation,
        IStructSchema<TInput, TInputBuilder> inputSchema,
        IStructSchema<TOutput, TOutputBuilder> outputSchema,
        IReadOnlyList<IOperationErrorSchema> modeledErrors,
        IRestBodyCodecFactory codecFactory,
        Func<SmithyHttpClientResponse, string?> errorDiscriminator,
        bool rawStringPayloads,
        string? errorTypeHeader,
        Action<SmithyHttpRequest>? requestTransform
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull =>
        new RestOperationProtocol<TInput, TOutput, TInputBuilder, TOutputBuilder>(
            RestOperationBinding.From(
                operation,
                inputSchema,
                outputSchema,
                codecFactory,
                rawStringPayloads
            ),
            modeledErrors,
            codecFactory,
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader,
            requestTransform
        );
}

/// <summary>
/// REST protocol bound to one operation. The <see cref="RestOperationBinding{TInput, TOutput}"/>
/// (parsed from the operation's <c>@http</c> trait, with body/payload codecs already compiled) is
/// built once and reused; all wire logic is delegated to <see cref="RestProtocol"/>.
/// </summary>
public sealed class RestOperationProtocol<TInput, TOutput, TInputBuilder, TOutputBuilder>(
    RestOperationBinding<TInput, TOutput, TInputBuilder, TOutputBuilder> binding,
    IReadOnlyList<IOperationErrorSchema> modeledErrors,
    IRestBodyCodecFactory codecFactory,
    Func<SmithyHttpClientResponse, string?> errorDiscriminator,
    bool rawStringPayloads,
    string? errorTypeHeader,
    Action<SmithyHttpRequest>? requestTransform = null,
    ISmithyValidator<TInput>? inputValidator = null
) : IOperationProtocol<TInput, TOutput>
    where TInputBuilder : notnull
    where TOutputBuilder : notnull
{
    public ISmithyValidator<TInput>? InputValidator { get; } = inputValidator;

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

    public SmithyHttpRequest SerializeRequest(
        TInput input,
        CancellationToken cancellationToken = default
    )
    {
        var request = RestProtocol.SerializeRequest(binding, input);
        requestTransform?.Invoke(request);
        return request;
    }

    public ValueTask<TOutput> DeserializeResponseAsync(
        SmithyHttpClientResponse response,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(RestProtocol.DeserializeResponse(binding, response));

    public ValueTask<TInput> DeserializeRequestAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(RestProtocol.DeserializeRequest(binding, request));

    public SmithyHttpServerResponse SerializeResponse(
        TOutput output,
        CancellationToken cancellationToken = default
    ) => RestProtocol.SerializeResponse(binding, output);

    public bool IsErrorResponse(SmithyHttpClientResponse response) =>
        (int)response.StatusCode >= 400;

    // REST errors may carry a discriminator header, but a response without one can still resolve
    // via the HTTP status code.
    public ValueTask<Exception?> DeserializeErrorAsync(
        SmithyHttpClientResponse response,
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

    public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
        serverErrors.TrySerialize(exception, out response);

    private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
        where TError : Exception =>
        (
            typeof(TError),
            exception =>
                RestProtocol.SerializeError(
                    error.Schema,
                    (TError)exception,
                    error.Id.ToString(),
                    error.HttpStatusCode,
                    codecFactory,
                    rawStringPayloads,
                    errorTypeHeader
                )
        );
}

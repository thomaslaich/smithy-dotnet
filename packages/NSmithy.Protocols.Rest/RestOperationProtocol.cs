using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Protocols.Rest;

/// <summary>
/// REST <see cref="IServiceProtocol"/> shared by restJson1 / simpleRestJson / restXml. A protocol is
/// described by three things: its body wire format (<paramref name="codecFactoryFor"/> hands out the
/// <see cref="IRestBodyCodecFactory"/> — JSON vs XML — for a read mode), whether string/enum payloads
/// are raw <c>text/plain</c> (<paramref name="rawStringPayloads"/> — true for restJson1/restXml,
/// false for simpleRestJson), and the modeled-error discriminator header
/// (<paramref name="errorTypeHeader"/>; <c>null</c> for protocols that don't serialize errors via a
/// header, such as restXml). Unlike rpcv2Cbor, REST's per-operation path is authored <c>@http</c>
/// data on the operation schema, so the service schema is not consulted here.
/// </summary>
public sealed class RestServiceProtocol(
    Func<WireReadMode, IRestBodyCodecFactory> codecFactoryFor,
    Func<SmithyHttpClientResponse, string?> errorDiscriminator,
    bool rawStringPayloads,
    string? errorTypeHeader,
    bool requiresDeclaredContentType = true
) : IServiceProtocol
{
    public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    ) => CreateOperation(operation, server: false);

    public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation
    ) => CreateOperation(operation, server: true);

    private IOperationProtocol<TInput, TOutput> CreateOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        bool server
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(codecFactoryFor);

        // Each side gets its own binding, so each side's codecs read by its own rules and only the
        // server compiles a validator.
        return CreateOperation(
            operation,
            operation.Errors,
            codecFactoryFor(server ? WireReadMode.Strict : WireReadMode.Lenient),
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader,
            SmithyRequestModifiers.Compile(operation),
            server ? SmithyValidator.FromSchema(operation.Input) : null,
            requiresDeclaredContentType
        );
    }

    private static IOperationProtocol<TInput, TOutput> CreateOperation<TInput, TOutput>(
        OperationSchema<TInput, TOutput> operation,
        IReadOnlyList<IOperationErrorSchema> modeledErrors,
        IRestBodyCodecFactory codecFactory,
        Func<SmithyHttpClientResponse, string?> errorDiscriminator,
        bool rawStringPayloads,
        string? errorTypeHeader,
        Action<SmithyHttpRequest>? requestTransform,
        ISmithyValidator<TInput>? inputValidator,
        bool requiresDeclaredContentType
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
            requestTransform,
            inputValidator,
            requiresDeclaredContentType
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
        Action<SmithyHttpRequest>? requestTransform,
        ISmithyValidator<TInput>? inputValidator,
        bool requiresDeclaredContentType
    )
        where TInputBuilder : notnull
        where TOutputBuilder : notnull =>
        new RestOperationProtocol<TInput, TOutput, TInputBuilder, TOutputBuilder>(
            RestOperationBinding.From(
                operation,
                inputSchema,
                outputSchema,
                codecFactory,
                rawStringPayloads,
                requiresDeclaredContentType
            ),
            modeledErrors,
            codecFactory,
            errorDiscriminator,
            rawStringPayloads,
            errorTypeHeader,
            requestTransform,
            inputValidator
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

    // One shape serves every malformed-request kind, differing only in shape id and status code, so
    // a single compiled writer covers them all. Compiled eagerly with the rest of the operation:
    // this is the validation-failure path, which for many services is ordinary traffic.
    private readonly RestErrorSerializer<MalformedRequestException>? malformedRequestSerializer =
        errorTypeHeader is null
            ? null
            : RestProtocol.CompileErrorSerializer(
                MalformedRequestSchema.Schema,
                codecFactory,
                rawStringPayloads,
                errorTypeHeader
            );

    public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response)
    {
        // A framework fault is not one of the operation's modeled errors — no model declares it —
        // so it is answered here rather than through the compiled matcher.
        if (
            exception is MalformedRequestException malformed
            && malformedRequestSerializer is not null
        )
        {
            var (errorType, statusCode) = MalformedRequestSchema.Wire(malformed.Kind);
            response = malformedRequestSerializer(malformed, errorType, statusCode);
            return true;
        }

        return serverErrors.TrySerialize(exception, out response);
    }

    private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error,
        IRestBodyCodecFactory codecFactory,
        bool rawStringPayloads,
        string errorTypeHeader
    )
        where TError : Exception
    {
        // Compiled here, where the rest of the operation's wire work is compiled, rather than inside
        // the returned closure. This previously called SerializeError per response, which re-derived
        // the shape's header/body member split and recompiled the projected body codec every time.
        var serialize = RestProtocol.CompileErrorSerializer(
            error.Schema,
            codecFactory,
            rawStringPayloads,
            errorTypeHeader
        );
        var errorShapeId = error.Id.ToString();
        var statusCode = error.HttpStatusCode;

        return (
            typeof(TError),
            exception => serialize((TError)exception, errorShapeId, statusCode)
        );
    }
}

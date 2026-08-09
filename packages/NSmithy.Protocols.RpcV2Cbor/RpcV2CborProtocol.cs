using System.Formats.Cbor;
using System.Net;
using System.Runtime.CompilerServices;
using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
using NSmithy.Http;

namespace NSmithy.Protocols.RpcV2Cbor;

public sealed class RpcV2CborProtocol : IProtocol
{
    private const string ContentType = "application/cbor";
    private const string EventStreamContentType = "application/vnd.amazon.eventstream";
    private const string InitialRequestEventType = "initial-request";
    private const string InitialResponseEventType = "initial-response";

    // Smithy 2.0 wraps `input: Unit` / `output: Unit` in synthetic structures that carry
    // this trait pointing back to the original `smithy.api#Unit` shape id.
    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );
    private static readonly string UnitShapeIdString = "smithy.api#Unit";

    /// <summary>Returns true for synthetic unit-derived schemas that carry no members.</summary>
    private static bool IsUnitSchema(Schema schema) =>
        schema.HasTrait(SyntheticOriginalShapeId)
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == UnitShapeIdString;

    /// <summary>
    /// Binds the protocol to a service, yielding per-operation protocols. The service schema
    /// supplies the service shape name used to derive each operation's request path.
    /// </summary>
    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service);
    }

    private sealed class ServiceProtocol(ServiceSchema service) : IServiceProtocol
    {
        public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            var inputEvent = FindEventStreamEventSchema(operation.Input);
            var outputEvent = FindEventStreamEventSchema(operation.Output);
            return (inputEvent, outputEvent) switch
            {
                (null, null) => new OperationProtocol<TInput, TOutput>(service, operation),
                (null, not null) => CreateOutputEventStreamProtocol(
                    operation,
                    (dynamic)outputEvent
                ),
                (not null, null) => CreateInputEventStreamProtocol(operation, (dynamic)inputEvent),
                (not null, not null) => CreateDuplexEventStreamProtocol(
                    operation,
                    (dynamic)inputEvent,
                    (dynamic)outputEvent
                ),
            };
        }

        private IOperationProtocol<TInput, TOutput> CreateOutputEventStreamProtocol<
            TInput,
            TOutput,
            TOutputEvent
        >(OperationSchema<TInput, TOutput> operation, Schema<TOutputEvent> outputEvent)
        {
            if (operation.Output.Resolved is not IStructSchema<TOutput> outputSchema)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor event stream output must use a structure shape."
                );
            }

            return CreateOutputEventStreamProtocol(operation, outputEvent, (dynamic)outputSchema);
        }

        private OutputEventStreamOperationProtocol<
            TInput,
            TOutput,
            TOutputEvent,
            TOutputBuilder
        > CreateOutputEventStreamProtocol<TInput, TOutput, TOutputEvent, TOutputBuilder>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent,
            IStructSchema<TOutput, TOutputBuilder> outputSchema
        )
            where TOutputBuilder : notnull =>
            new OutputEventStreamOperationProtocol<TInput, TOutput, TOutputEvent, TOutputBuilder>(
                service,
                operation,
                outputEvent,
                outputSchema
            );

        private IOperationProtocol<TInput, TOutput> CreateInputEventStreamProtocol<
            TInput,
            TInputEvent,
            TOutput
        >(OperationSchema<TInput, TOutput> operation, Schema<TInputEvent> inputEvent)
        {
            if (operation.Input.Resolved is not IStructSchema<TInput> inputSchema)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor event stream input must use a structure shape."
                );
            }

            return CreateInputEventStreamProtocol(operation, inputEvent, (dynamic)inputSchema);
        }

        private InputEventStreamOperationProtocol<
            TInput,
            TInputEvent,
            TOutput,
            TInputBuilder
        > CreateInputEventStreamProtocol<TInput, TInputEvent, TOutput, TInputBuilder>(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            IStructSchema<TInput, TInputBuilder> inputSchema
        )
            where TInputBuilder : notnull =>
            new InputEventStreamOperationProtocol<TInput, TInputEvent, TOutput, TInputBuilder>(
                service,
                operation,
                inputEvent,
                inputSchema
            );

        private IOperationProtocol<TInput, TOutput> CreateDuplexEventStreamProtocol<
            TInput,
            TOutput,
            TInputEvent,
            TOutputEvent
        >(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent
        )
        {
            if (operation.Input.Resolved is not IStructSchema<TInput> inputSchema)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor event stream input must use a structure shape."
                );
            }

            if (operation.Output.Resolved is not IStructSchema<TOutput> outputSchema)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor event stream output must use a structure shape."
                );
            }

            return CreateDuplexEventStreamProtocol(
                operation,
                inputEvent,
                outputEvent,
                (dynamic)inputSchema,
                (dynamic)outputSchema
            );
        }

        private DuplexEventStreamOperationProtocol<
            TInput,
            TOutput,
            TInputEvent,
            TOutputEvent,
            TInputBuilder,
            TOutputBuilder
        > CreateDuplexEventStreamProtocol<
            TInput,
            TOutput,
            TInputEvent,
            TOutputEvent,
            TInputBuilder,
            TOutputBuilder
        >(
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent,
            IStructSchema<TInput, TInputBuilder> inputSchema,
            IStructSchema<TOutput, TOutputBuilder> outputSchema
        )
            where TInputBuilder : notnull
            where TOutputBuilder : notnull =>
            new DuplexEventStreamOperationProtocol<
                TInput,
                TOutput,
                TInputEvent,
                TOutputEvent,
                TInputBuilder,
                TOutputBuilder
            >(service, operation, inputEvent, outputEvent, inputSchema, outputSchema);
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private readonly string requestUri;
        private readonly bool inputIsUnit;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly ICborCodec<TInput> requestCodec;
        private readonly ICborCodec<TOutput> responseCodec;
        private readonly Action<SmithyHttpRequest>? requestTransform;
        private readonly ModeledErrorSerializer serverErrors;

        public OperationProtocol(ServiceSchema service, OperationSchema<TInput, TOutput> operation)
        {
            requestTransform = SmithyRequestModifiers.Compile(operation);
            // The rpcv2Cbor path is service-derived; the protocol owns this wire detail.
            requestUri = $"/service/{service.Id.Name}/operation/{operation.Id.Name}";
            inputIsUnit = typeof(TInput) == typeof(SmithyUnit) || IsUnitSchema(operation.Input);
            outputIsSmithyUnit = typeof(TOutput) == typeof(SmithyUnit);
            outputIsUnit = outputIsSmithyUnit || IsUnitSchema(operation.Output);

            // Built once per operation; the right materialize policy baked in per direction.
            requestCodec = CborCodec.FromSchema(
                operation.Input,
                materializeTopLevelDefaults: false
            );
            responseCodec = CborCodec.FromSchema(
                operation.Output,
                materializeTopLevelDefaults: true
            );
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = BaseRequest(requestUri, ContentType);
            if (!inputIsUnit)
            {
                request.Body = new SmithyHttpBody.Bytes(requestCodec.Serialize(input));
                request.ContentType = ContentType;
            }

            requestTransform?.Invoke(request);
            return request;
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            if (outputIsSmithyUnit)
            {
                EnsureResponse(response);
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            return ValueTask.FromResult(
                response.Content.Length == 0
                    ? default!
                    : responseCodec.Deserialize(response.Content)
            );
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return ValueTask.FromResult<TInput>(default!);
            }

            var content = BodyBytes(request.Body);
            return ValueTask.FromResult(
                content.Length == 0 ? default! : requestCodec.Deserialize(content)
            );
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            if (outputIsUnit)
            {
                return BufferedResponse(
                    200,
                    ReadOnlyMemory<byte>.Empty,
                    headers => headers["Smithy-Protocol"] = ["rpc-v2-cbor"]
                );
            }

            return BufferedResponse(
                200,
                responseCodec.Serialize(output),
                headers =>
                {
                    headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                    headers["Content-Type"] = [ContentType];
                }
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        // rpcv2Cbor errors always carry an explicit __type discriminator; without one the
        // response carries no modeled error, and the HTTP status maps to no error shape.
        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                OperationProtocolErrors.DeserializeModeledError(
                    HttpErrors,
                    response,
                    r => HasResponse(r) ? DeserializeErrorType(r) : null,
                    requiresErrorDiscriminator: true,
                    supportsHttpStatusErrorFallback: false
                )
            );

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);

        private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
            OperationErrorSchema<TError> error
        )
            where TError : Exception =>
            (
                typeof(TError),
                exception =>
                    RpcV2CborProtocol.SerializeError(
                        error.Schema,
                        (TError)exception,
                        error.Id.ToString(),
                        error.HttpStatusCode
                    )
            );

        private static HttpOperationError[] CompileErrors(
            IReadOnlyList<IOperationErrorSchema> errors
        ) => errors.Select(error => (HttpOperationError)CompileError((dynamic)error)).ToArray();

        private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
            where TError : Exception
        {
            var codec = CborCodec.FromSchema(error.Schema);
            return new HttpOperationError(
                error.Id,
                error.HttpStatusCode,
                response => DeserializeRequiredBody(codec, response.Content)
            );
        }

        private static byte[] BodyBytes(SmithyHttpBody body) =>
            body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];
    }

    private sealed class OutputEventStreamOperationProtocol<
        TInput,
        TOutput,
        TOutputEvent,
        TOutputBuilder
    > : IOperationProtocol<TInput, TOutput>
        where TOutputBuilder : notnull
    {
        private readonly string requestUri;
        private readonly bool inputIsUnit;
        private readonly ICborCodec<TInput>? requestCodec;
        private readonly ICborCodec<TOutputEvent> responseCodec;
        private readonly IUnionSchema responseEvent;
        private readonly EventStreamShapeBinding<
            TOutput,
            TOutputEvent,
            TOutputBuilder
        > outputBinding;
        private readonly ModeledErrorSerializer serverErrors;

        public OutputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TOutputEvent> outputEvent,
            IStructSchema<TOutput, TOutputBuilder> outputSchema
        )
        {
            requestUri = RequestUri(service, operation);
            inputIsUnit = IsUnit<TInput>(operation.Input);
            requestCodec = inputIsUnit
                ? null
                : CborCodec.FromSchema(operation.Input, materializeTopLevelDefaults: false);
            responseCodec = CborCodec.FromSchema(outputEvent);
            responseEvent =
                outputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            outputBinding = EventStreamShapeBinding<TOutput, TOutputEvent, TOutputBuilder>.Create(
                outputSchema,
                materializeTopLevelDefaults: true
            );
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = BaseRequest(requestUri, EventStreamContentType);
            request.ExpectStreamingResponse = true;
            if (!inputIsUnit)
            {
                request.Body = new SmithyHttpBody.Bytes(requestCodec!.Serialize(input));
                request.ContentType = ContentType;
            }

            return request;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            if (inputIsUnit)
            {
                return ValueTask.FromResult<TInput>(default!);
            }

            var content = BodyBytes(request.Body);
            return ValueTask.FromResult(
                content.Length == 0 ? default! : requestCodec!.Deserialize(content)
            );
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureEventStreamResponse(response);
            return ReadShapeAsync(
                ResponseStream(response),
                disposeBody: true,
                outputBinding,
                responseCodec,
                InitialResponseEventType,
                cancellationToken
            );
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingResponse(
                FrameShapeAsync(
                    output,
                    outputBinding,
                    responseCodec,
                    responseEvent,
                    InitialResponseEventType,
                    cancellationToken
                )
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    private sealed class InputEventStreamOperationProtocol<
        TInput,
        TInputEvent,
        TOutput,
        TInputBuilder
    > : IOperationProtocol<TInput, TOutput>
        where TInputBuilder : notnull
    {
        private readonly string requestUri;
        private readonly bool outputIsUnit;
        private readonly ICborCodec<TInputEvent> requestCodec;
        private readonly IUnionSchema requestEvent;
        private readonly ICborCodec<TOutput>? responseCodec;
        private readonly EventStreamShapeBinding<TInput, TInputEvent, TInputBuilder> inputBinding;
        private readonly ModeledErrorSerializer serverErrors;

        public InputEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            IStructSchema<TInput, TInputBuilder> inputSchema
        )
        {
            requestUri = RequestUri(service, operation);
            outputIsUnit = IsUnit<TOutput>(operation.Output);
            requestCodec = CborCodec.FromSchema(inputEvent);
            requestEvent =
                inputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            responseCodec = outputIsUnit ? null : CborCodec.FromSchema(operation.Output);
            inputBinding = EventStreamShapeBinding<TInput, TInputEvent, TInputBuilder>.Create(
                inputSchema,
                materializeTopLevelDefaults: false
            );
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            return BaseStreamingRequest(
                requestUri,
                ContentType,
                FrameShapeAsync(
                    input,
                    inputBinding,
                    requestCodec,
                    requestEvent,
                    InitialRequestEventType,
                    cancellationToken
                )
            );
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ReadShapeAsync(
                RequestStream(request),
                disposeBody: false,
                inputBinding,
                requestCodec,
                InitialRequestEventType,
                cancellationToken
            );
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureResponse(response);
            if (outputIsUnit)
            {
                DisposeResponseBody(response);
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            return DeserializeSingleResponseAsync(response, responseCodec!, cancellationToken);
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            if (outputIsUnit)
            {
                return BufferedResponse(
                    200,
                    ReadOnlyMemory<byte>.Empty,
                    headers => headers["Smithy-Protocol"] = ["rpc-v2-cbor"]
                );
            }

            return BufferedResponse(
                200,
                responseCodec!.Serialize(output),
                headers =>
                {
                    headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                    headers["Content-Type"] = [ContentType];
                }
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    private sealed class DuplexEventStreamOperationProtocol<
        TInput,
        TOutput,
        TInputEvent,
        TOutputEvent,
        TInputBuilder,
        TOutputBuilder
    > : IOperationProtocol<TInput, TOutput>
        where TInputBuilder : notnull
        where TOutputBuilder : notnull
    {
        private readonly string requestUri;
        private readonly ICborCodec<TInputEvent> requestCodec;
        private readonly IUnionSchema requestEvent;
        private readonly ICborCodec<TOutputEvent> responseCodec;
        private readonly IUnionSchema responseEvent;
        private readonly EventStreamShapeBinding<TInput, TInputEvent, TInputBuilder> inputBinding;
        private readonly EventStreamShapeBinding<
            TOutput,
            TOutputEvent,
            TOutputBuilder
        > outputBinding;
        private readonly ModeledErrorSerializer serverErrors;

        public DuplexEventStreamOperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            Schema<TInputEvent> inputEvent,
            Schema<TOutputEvent> outputEvent,
            IStructSchema<TInput, TInputBuilder> inputSchema,
            IStructSchema<TOutput, TOutputBuilder> outputSchema
        )
        {
            requestUri = RequestUri(service, operation);
            requestCodec = CborCodec.FromSchema(inputEvent);
            requestEvent =
                inputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            responseCodec = CborCodec.FromSchema(outputEvent);
            responseEvent =
                outputEvent.Resolved as IUnionSchema
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor event streams must target a union schema."
                );
            inputBinding = EventStreamShapeBinding<TInput, TInputEvent, TInputBuilder>.Create(
                inputSchema,
                materializeTopLevelDefaults: false
            );
            outputBinding = EventStreamShapeBinding<TOutput, TOutputEvent, TOutputBuilder>.Create(
                outputSchema,
                materializeTopLevelDefaults: true
            );
            HttpErrors = CompileErrors(operation.Errors);
            serverErrors = ModeledErrorSerializer.Compile(
                operation.Errors,
                error => CompileServerError((dynamic)error)
            );
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(input);
            // Duplex streams both directions, so the response must be read in Stream mode.
            var request = BaseStreamingRequest(
                requestUri,
                EventStreamContentType,
                FrameShapeAsync(
                    input,
                    inputBinding,
                    requestCodec,
                    requestEvent,
                    InitialRequestEventType,
                    cancellationToken
                )
            );
            request.ExpectStreamingResponse = true;
            return request;
        }

        public ValueTask<TInput> DeserializeRequestAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            return ReadShapeAsync(
                RequestStream(request),
                disposeBody: false,
                inputBinding,
                requestCodec,
                InitialRequestEventType,
                cancellationToken
            );
        }

        public ValueTask<TOutput> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            EnsureEventStreamResponse(response);
            return ReadShapeAsync(
                ResponseStream(response),
                disposeBody: true,
                outputBinding,
                responseCodec,
                InitialResponseEventType,
                cancellationToken
            );
        }

        public SmithyHttpServerResponse SerializeResponse(
            TOutput output,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(output);
            return StreamingResponse(
                FrameShapeAsync(
                    output,
                    outputBinding,
                    responseCodec,
                    responseEvent,
                    InitialResponseEventType,
                    cancellationToken
                )
            );
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => DeserializeModeledErrorAsync(HttpErrors, response);

        public bool TrySerializeError(Exception exception, out SmithyHttpServerResponse response) =>
            serverErrors.TrySerialize(exception, out response);
    }

    /// <summary>
    /// Serializes a modeled error into a rpcv2Cbor error response: a CBOR body carrying a
    /// <c>__type</c> discriminator (the absolute shape id) plus the error's members, with the
    /// supplied HTTP status code and the protocol header.
    /// </summary>
    public static SmithyHttpServerResponse SerializeError<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId,
        int statusCode
    )
    {
        ArgumentNullException.ThrowIfNull(errorSchema);
        ArgumentNullException.ThrowIfNull(errorShapeId);

        return BufferedResponse(
            statusCode,
            SerializeErrorBody(errorSchema, error, errorShapeId),
            headers =>
            {
                headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
                headers["Content-Type"] = [ContentType];
            }
        );
    }

    private static byte[] SerializeErrorBody<TError>(
        Schema<TError> errorSchema,
        TError error,
        string errorShapeId
    )
    {
        if (errorSchema.Resolved is not IStructSchema<TError> structSchema)
        {
            throw new InvalidOperationException(
                "rpcv2Cbor errors must be backed by a structure schema."
            );
        }

        var visitor = new CborMemberWriterCompiler<TError>(
            new CborWriterCompiler(materializeTopLevelDefaults: true),
            materializeDefaults: true
        );
        structSchema.VisitMembers(visitor);

        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(null);
        writer.WriteTextString("__type");
        writer.WriteTextString(errorShapeId);
        foreach (var memberWriter in visitor.Writers)
        {
            memberWriter.Write(writer, error);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    private static ValueTask<Exception?> DeserializeModeledErrorAsync(
        IReadOnlyList<HttpOperationError> httpErrors,
        SmithyHttpClientResponse response
    ) =>
        ValueTask.FromResult(
            OperationProtocolErrors.DeserializeModeledError(
                httpErrors,
                response,
                r => HasResponse(r) ? DeserializeErrorType(r) : null,
                requiresErrorDiscriminator: true,
                supportsHttpStatusErrorFallback: false
            )
        );

    private static (Type, Func<Exception, SmithyHttpServerResponse>) CompileServerError<TError>(
        OperationErrorSchema<TError> error
    )
        where TError : Exception =>
        (
            typeof(TError),
            exception =>
                SerializeError(
                    error.Schema,
                    (TError)exception,
                    error.Id.ToString(),
                    error.HttpStatusCode
                )
        );

    private static HttpOperationError[] CompileErrors(
        IReadOnlyList<IOperationErrorSchema> errors
    ) => errors.Select(error => (HttpOperationError)CompileError((dynamic)error)).ToArray();

    private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
        where TError : Exception
    {
        var codec = CborCodec.FromSchema(error.Schema);
        return new HttpOperationError(
            error.Id,
            error.HttpStatusCode,
            response => DeserializeRequiredBody(codec, response.Content)
        );
    }

    private static SmithyHttpServerResponse BufferedResponse(
        int statusCode,
        ReadOnlyMemory<byte> body,
        Action<IDictionary<string, IReadOnlyList<string>>>? headers = null
    )
    {
        var response = new SmithyHttpServerResponse
        {
            StatusCode = statusCode,
            Body = SingleChunk(body),
            ContentLength = body.Length,
        };
        headers?.Invoke(response.Headers);
        return response;
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> SingleChunk(
        ReadOnlyMemory<byte> chunk
    )
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return chunk;
    }

    private static SmithyHttpRequest BaseRequest(string requestUri, string accept)
    {
        var request = new SmithyHttpRequest(HttpMethod.Post, requestUri);
        request.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        request.Headers["Accept"] = [accept];
        return request;
    }

    private static SmithyHttpRequest BaseStreamingRequest(
        string requestUri,
        string accept,
        IAsyncEnumerable<ReadOnlyMemory<byte>> body
    )
    {
        var request = BaseRequest(requestUri, accept);
        request.Body = new SmithyHttpBody.EventStreaming(body);
        request.ContentType = EventStreamContentType;
        return request;
    }

    private static SmithyHttpServerResponse StreamingResponse(
        IAsyncEnumerable<ReadOnlyMemory<byte>> body
    )
    {
        var response = new SmithyHttpServerResponse { StatusCode = 200, Body = body };
        response.Headers["Smithy-Protocol"] = ["rpc-v2-cbor"];
        response.Headers["Content-Type"] = [EventStreamContentType];
        return response;
    }

    private static string RequestUri<TInput, TOutput>(
        ServiceSchema service,
        OperationSchema<TInput, TOutput> operation
    ) => $"/service/{service.Id.Name}/operation/{operation.Id.Name}";

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameEventsAsync<T>(
        IAsyncEnumerable<T> events,
        ICborCodec<T> codec,
        IUnionSchema eventSchema,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            var eventType = eventSchema.GetCaseObject(value!).Name;
            yield return CreateEventStreamMessage(eventType, codec.Serialize(value)).Encode();
        }
    }

    private sealed class EventStreamShapeBinding<TShape, TEvent, TBuilder>
        where TBuilder : notnull
    {
        private EventStreamShapeBinding(
            IStructSchema<TShape, TBuilder> structure,
            IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> streamMember,
            IProjectionCodec<TShape, TBuilder> initialCodec,
            bool hasInitialMembers
        )
        {
            Structure = structure;
            StreamMember = streamMember;
            InitialCodec = initialCodec;
            HasInitialMembers = hasInitialMembers;
        }

        public IStructSchema<TShape, TBuilder> Structure { get; }

        public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> StreamMember { get; }

        public IProjectionCodec<TShape, TBuilder> InitialCodec { get; }

        public bool HasInitialMembers { get; }

        public static EventStreamShapeBinding<TShape, TEvent, TBuilder> Create(
            IStructSchema<TShape, TBuilder> structure,
            bool materializeTopLevelDefaults
        )
        {
            var streamMember = FindStreamMember(structure);
            var initialMembers = structure
                .TypedMembers.Where(member => !ReferenceEquals(member, streamMember))
                .ToArray();
            return new EventStreamShapeBinding<TShape, TEvent, TBuilder>(
                structure,
                streamMember,
                CborCodec.FromProjection(
                    Schemas.Project(structure, initialMembers),
                    materializeTopLevelDefaults
                ),
                initialMembers.Length > 0
            );
        }

        public IAsyncEnumerable<TEvent> GetEvents(TShape shape) =>
            StreamMember.GetValue(shape)
            ?? throw new InvalidOperationException(
                $"Event stream member '{StreamMember.Name}' was null."
            );

        public TShape Build(byte[] initialPayload, IAsyncEnumerable<TEvent> events)
        {
            var builder = Structure.CreateTypedBuilder();
            if (initialPayload.Length > 0)
            {
                InitialCodec.ReadInto(initialPayload, builder);
            }

            StreamMember.SetValue(builder, events);
            return Structure.Build(builder);
        }

        private static IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>> FindStreamMember(
            IStructSchema<TShape, TBuilder> structure
        )
        {
            var visitor = new EventStreamMemberVisitor<TShape, TBuilder, TEvent>();
            structure.VisitMembers(visitor);
            return visitor.Member
                ?? throw new InvalidOperationException(
                    "rpcv2Cbor initial event streams require one event stream member."
                );
        }
    }

    private sealed class EventStreamMemberVisitor<TShape, TBuilder, TEvent>
        : IMemberVisitor<TShape, TBuilder>
    {
        public IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>? Member
        {
            get;
            private set;
        }

        public void Visit<TValue>(IMemberSchema<TShape, TBuilder, TValue> member)
        {
            if (member.Target is not EventStreamSchema<TEvent>)
            {
                return;
            }

            if (Member is not null)
            {
                throw new InvalidOperationException(
                    "rpcv2Cbor initial event streams require one event stream member."
                );
            }

            Member = (IMemberSchema<TShape, TBuilder, IAsyncEnumerable<TEvent>>)(object)member;
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FrameShapeAsync<
        TShape,
        TEvent,
        TBuilder
    >(
        TShape shape,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICborCodec<TEvent> eventCodec,
        IUnionSchema eventSchema,
        string initialEventType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
        where TBuilder : notnull
    {
        if (binding.HasInitialMembers)
        {
            yield return CreateEventStreamMessage(
                    initialEventType,
                    binding.InitialCodec.Serialize(shape)
                )
                .Encode();
        }

        await foreach (
            var chunk in FrameEventsAsync(
                    binding.GetEvents(shape),
                    eventCodec,
                    eventSchema,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )
        {
            yield return chunk;
        }
    }

    private static EventStreamMessage CreateEventStreamMessage(
        string eventType,
        ReadOnlyMemory<byte> payload
    ) =>
        new(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.MessageType] = new EventStreamHeaderValue.Text(
                    EventStreamHeaders.EventMessageType
                ),
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text(eventType),
                [EventStreamHeaders.ContentType] = new EventStreamHeaderValue.Text(ContentType),
            },
            payload
        );

    private static async ValueTask<TShape> ReadShapeAsync<TShape, TEvent, TBuilder>(
        Stream body,
        bool disposeBody,
        EventStreamShapeBinding<TShape, TEvent, TBuilder> binding,
        ICborCodec<TEvent> eventCodec,
        string initialEventType,
        CancellationToken cancellationToken
    )
        where TBuilder : notnull
    {
        var enumerator = EventStreamMessageReader
            .ReadAllAsync(body, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        EventStreamMessage? firstEvent = null;
        var initialPayload = ReadOnlyMemory<byte>.Empty;
        var ownsEnumerator = true;

        try
        {
            if (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                var first = enumerator.Current;
                EnsureEventMessage(first);
                var eventType = first.StringHeader(EventStreamHeaders.EventType);
                if (string.Equals(eventType, initialEventType, StringComparison.Ordinal))
                {
                    EnsureCborPayload(first);
                    initialPayload = first.Payload;
                }
                else
                {
                    firstEvent = first;
                }
            }

            var shape = binding.Build(
                initialPayload.ToArray(),
                ReadRemainingEventsAsync(
                    firstEvent,
                    enumerator,
                    body,
                    disposeBody,
                    eventCodec,
                    cancellationToken
                )
            );
            ownsEnumerator = false;
            return shape;
        }
        finally
        {
            if (ownsEnumerator)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
                if (disposeBody)
                {
                    await body.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        static async IAsyncEnumerable<TEvent> ReadRemainingEventsAsync(
            EventStreamMessage? first,
            IAsyncEnumerator<EventStreamMessage> enumerator,
            Stream body,
            bool disposeBody,
            ICborCodec<TEvent> eventCodec,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await using (enumerator.ConfigureAwait(false))
            {
                try
                {
                    if (first is not null)
                    {
                        var firstValue = DeserializeEventMessage(eventCodec, first);
                        if (firstValue is not null)
                        {
                            yield return firstValue;
                        }
                    }

                    while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        var value = DeserializeEventMessage(eventCodec, enumerator.Current);
                        if (value is not null)
                        {
                            yield return value;
                        }
                    }
                }
                finally
                {
                    if (disposeBody)
                    {
                        await body.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private static async ValueTask<T> DeserializeSingleResponseAsync<T>(
        SmithyHttpClientResponse response,
        ICborCodec<T> codec,
        CancellationToken cancellationToken
    )
    {
        var body = ResponseStream(response);
        await using (body.ConfigureAwait(false))
        {
            using var stream = new MemoryStream();
            await body.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            var content = stream.ToArray();
            return content.Length == 0 ? default! : codec.Deserialize(content);
        }
    }

    private static T? DeserializeEventMessage<T>(ICborCodec<T> codec, EventStreamMessage message)
    {
        EnsureEventMessage(message);
        EnsureCborPayload(message);
        return codec.Deserialize(message.Payload.ToArray());
    }

    private static void EnsureEventMessage(EventStreamMessage message)
    {
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        if (
            !string.Equals(
                messageType,
                EventStreamHeaders.EventMessageType,
                StringComparison.Ordinal
            )
        )
        {
            ThrowEventStreamException(message);
        }
    }

    private static void EnsureCborPayload(EventStreamMessage message)
    {
        var contentType = message.StringHeader(EventStreamHeaders.ContentType);
        if (
            contentType is not null
            && !string.Equals(contentType, ContentType, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException(
                $"Expected rpcv2Cbor event payload content type '{ContentType}' but received '{contentType}'."
            );
        }
    }

    private static void ThrowEventStreamException(EventStreamMessage message)
    {
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        if (
            string.Equals(
                messageType,
                EventStreamHeaders.ErrorMessageType,
                StringComparison.Ordinal
            )
        )
        {
            var code = message.StringHeader(EventStreamHeaders.ErrorCode) ?? "UnknownError";
            var text = message.StringHeader(EventStreamHeaders.ErrorMessage);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(text) ? code : $"{code}: {text}"
            );
        }

        if (
            string.Equals(
                messageType,
                EventStreamHeaders.ExceptionMessageType,
                StringComparison.Ordinal
            )
        )
        {
            var type = message.StringHeader(EventStreamHeaders.ExceptionType) ?? "UnknownException";
            throw new InvalidOperationException($"rpcv2Cbor event stream exception: {type}.");
        }

        throw new InvalidDataException(
            $"Unknown rpcv2Cbor event stream message type '{messageType ?? "<missing>"}'."
        );
    }

    private static Stream RequestStream(SmithyHttpRequest request) =>
        request.Body switch
        {
            SmithyHttpBody.Streaming streaming => streaming.Content,
            SmithyHttpBody.Bytes bytes => new MemoryStream(bytes.Content, writable: false),
            _ => Stream.Null,
        };

    private static Stream ResponseStream(SmithyHttpClientResponse response) =>
        response.Body switch
        {
            SmithyHttpBody.Streaming streaming => streaming.Content,
            SmithyHttpBody.Bytes bytes => new MemoryStream(bytes.Content, writable: false),
            _ => Stream.Null,
        };

    private static void DisposeResponseBody(SmithyHttpClientResponse response)
    {
        if (response.Body is SmithyHttpBody.Streaming streaming)
        {
            streaming.Content.Dispose();
        }
    }

    private static byte[] BodyBytes(SmithyHttpBody body) =>
        body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

    private static void EnsureEventStreamResponse(SmithyHttpClientResponse response)
    {
        EnsureResponse(response);
        if (response.StatusCode == HttpStatusCode.OK && IsEventStreamContentType(response))
        {
            return;
        }

        DisposeResponseBody(response);
        throw new InvalidOperationException(
            $"Expected a rpcv2Cbor event stream response but received HTTP {(int)response.StatusCode}."
        );
    }

    private static bool IsEventStreamContentType(SmithyHttpClientResponse response) =>
        response.ContentHeaders.TryGetValue("Content-Type", out var contentType)
        && contentType.Any(value =>
            value.StartsWith(EventStreamContentType, StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsUnit<T>(Schema schema) =>
        typeof(T) == typeof(SmithyUnit) || IsUnitSchema(schema);

    private static Schema? FindEventStreamEventSchema(Schema schema) =>
        schema.Resolved is IStructSchema structure
            ? structure
                .Members.Select(member => member.Target)
                .OfType<IEventStreamSchema>()
                .Select(eventStream => eventStream.EventSchema)
                .SingleOrDefault()
            : null;

    private static T DeserializeRequiredBody<T>(ICodec<T> codec, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Response body is required but was empty.");
        }

        return codec.Deserialize(content);
    }

    public static bool HasResponse(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValue("Smithy-Protocol", out var values)
            && values.Any(value => string.Equals(value, "rpc-v2-cbor", StringComparison.Ordinal));
    }

    public static void EnsureResponse(SmithyHttpClientResponse response)
    {
        if (!HasResponse(response))
        {
            throw new InvalidOperationException(
                "rpcv2Cbor response is missing the required Smithy-Protocol header."
            );
        }
    }

    public static string? DeserializeErrorType(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return DeserializeErrorType(response.Content);
    }

    public static string? DeserializeErrorType(byte[] content)
    {
        if (content.Length == 0)
            return null;
        try
        {
            var reader = new System.Formats.Cbor.CborReader(
                content,
                System.Formats.Cbor.CborConformanceMode.Lax
            );
            if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)
                return null;
            reader.ReadStartMap();
            while (reader.PeekState() != System.Formats.Cbor.CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                if (string.Equals(key, "__type", StringComparison.Ordinal))
                    return reader.ReadTextString();
                reader.SkipValue();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

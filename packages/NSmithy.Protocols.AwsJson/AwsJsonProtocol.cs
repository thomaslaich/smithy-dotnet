using System.Text.Json;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.AwsJson;

public sealed class AwsJson10Protocol : AwsJsonProtocol
{
    public AwsJson10Protocol()
        : base("application/x-amz-json-1.0") { }
}

public sealed class AwsJson11Protocol : AwsJsonProtocol
{
    public AwsJson11Protocol()
        : base("application/x-amz-json-1.1") { }
}

public abstract class AwsJsonProtocol(string contentType) : IProtocol
{
    // AWS JSON 1.0/1.1 use modeled member names and explicitly ignore @jsonName. REST JSON and
    // generic JSON documents continue to honor the trait through JsonCodecFactory.Default.
    private static readonly JsonCodecFactory CodecFactory = new(honorJsonNameTrait: false);

    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service, contentType);
    }

    private sealed class ServiceProtocol(ServiceSchema service, string contentType)
        : IServiceProtocol
    {
        public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new OperationProtocol<TInput, TOutput>(service, operation, contentType);
        }

        public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) => throw new NotSupportedException("AWS JSON does not support serving operations.");
    }

    private sealed class OperationProtocol<TInput, TOutput>
        : IClientOperationProtocol<TInput, TOutput>
    {
        private static readonly byte[] EmptyJsonObject = "{}"u8.ToArray();

        private readonly string contentType;
        private readonly string target;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly Schema<TOutput> outputSchema;
        private readonly ICodec<TInput> requestCodec;
        private readonly ICodec<TOutput> responseCodec;
        private readonly Action<SmithyHttpRequest>? requestTransform;

        public OperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            string contentType
        )
        {
            this.contentType = contentType;
            target = $"{service.Id.Name}.{operation.Id.Name}";
            outputIsSmithyUnit = typeof(TOutput) == typeof(SmithyUnit);
            outputIsUnit = outputIsSmithyUnit || Schemas.IsSyntheticUnit(operation.Output);
            outputSchema = operation.Output;
            requestCodec = CodecFactory.FromSchema(operation.Input);
            responseCodec = CodecFactory.FromSchema(operation.Output);
            requestTransform = SmithyRequestModifiers.Compile(operation);
            HttpErrors = CompileErrors(operation.Errors);
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, "/")
            {
                Body = new SmithyHttpBody.Bytes(
                    typeof(TInput) == typeof(SmithyUnit)
                        ? EmptyJsonObject
                        : requestCodec.Serialize(input)
                ),
                ContentType = contentType,
            };
            request.Headers["Accept"] = [contentType];
            request.Headers["X-Amz-Target"] = [target];
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
                return ValueTask.FromResult((TOutput)(object)SmithyUnit.Value);
            }

            if (outputIsUnit)
            {
                return ValueTask.FromResult(CreateEmptyOutput(outputSchema));
            }

            if (response.Content.Length == 0)
            {
                return ValueTask.FromResult(CreateEmptyOutput(outputSchema));
            }

            var output = responseCodec.Deserialize(response.Content);
            return ValueTask.FromResult(output is null ? CreateEmptyOutput(outputSchema) : output);
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        // AWS JSON errors carry a __type/code discriminator in the body, but a response without
        // one can still resolve via the HTTP status code.
        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) =>
            ValueTask.FromResult(
                OperationProtocolErrors.DeserializeModeledError(
                    HttpErrors,
                    response,
                    DeserializeErrorType,
                    requiresErrorDiscriminator: false,
                    supportsHttpStatusErrorFallback: true
                )
            );

        private static TOutput CreateEmptyOutput(Schema<TOutput> schema) =>
            schema.Resolved is IStructSchema<TOutput> structSchema
                ? structSchema.BuildEmpty()
                : default!;

        private static HttpOperationError[] CompileErrors(
            IReadOnlyList<IOperationErrorSchema> errors
        ) => errors.Select(error => error.Accept(ErrorCompiler.Instance)).ToArray();

        private sealed class ErrorCompiler : IOperationErrorSchemaVisitor<HttpOperationError>
        {
            public static ErrorCompiler Instance { get; } = new();

            public HttpOperationError Visit<TError>(OperationErrorSchema<TError> error)
                where TError : Exception => CompileError(error);
        }

        private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
            where TError : Exception
        {
            var codec = CodecFactory.FromSchema(error.Schema);
            return new HttpOperationError(
                error.Id,
                error.HttpStatusCode,
                response =>
                    response.Content.Length == 0
                        ? CreateEmptyError<TError>()
                        : codec.Deserialize(response.Content)
            );
        }
    }

    public static string? DeserializeErrorType(SmithyHttpClientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var headerValue =
            TryGetFirstHeaderValue(response.Headers, "X-Amzn-Errortype")
            ?? TryGetFirstHeaderValue(response.ContentHeaders, "X-Amzn-Errortype");
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return NormalizeErrorType(headerValue);
        }

        if (response.Content.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (
                document.RootElement.TryGetProperty("__type", out var dunderType)
                && dunderType.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(dunderType.GetString());
            }

            if (
                document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
            )
            {
                return NormalizeErrorType(code.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static TError CreateEmptyError<TError>()
    {
        var type = typeof(TError);
        try
        {
            if (Activator.CreateInstance(type) is TError parameterless)
            {
                return parameterless;
            }
        }
        catch (MissingMethodException)
        {
            // Generated error types always accept a nullable message constructor, but not every
            // runtime type exposes a parameterless constructor.
        }

        if (Activator.CreateInstance(type, [null]) is TError messageOnly)
        {
            return messageOnly;
        }

        return default!;
    }

    private static string? TryGetFirstHeaderValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string name
    )
    {
        foreach (var header in headers)
        {
            if (
                string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                && header.Value.Count > 0
            )
            {
                return header.Value[0];
            }
        }

        return null;
    }

    private static string NormalizeErrorType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            text = text[..colon];
        }

        var hash = text.LastIndexOf('#');
        if (hash >= 0)
        {
            text = text[(hash + 1)..];
        }

        return text;
    }
}

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
    private static readonly ShapeId SyntheticOriginalShapeId = new(
        "smithy.synthetic",
        "originalShapeId"
    );

    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return new ServiceProtocol(service, contentType);
    }

    private static bool IsUnitSchema(Schema schema) =>
        schema.HasTrait(SyntheticOriginalShapeId)
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.Kind == DocumentKind.String
        && schema.GetTrait(SyntheticOriginalShapeId)?.Value.AsString() == "smithy.api#Unit";

    private sealed class ServiceProtocol(ServiceSchema service, string contentType)
        : IServiceProtocol
    {
        public IOperationProtocol<TInput, TOutput> ForOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new OperationProtocol<TInput, TOutput>(service, operation, contentType);
        }
    }

    private sealed class OperationProtocol<TInput, TOutput> : IOperationProtocol<TInput, TOutput>
    {
        private static readonly byte[] EmptyJsonObject = "{}"u8.ToArray();

        private readonly string contentType;
        private readonly string target;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly Schema<TOutput> outputSchema;
        private readonly IJsonCodec<TInput> requestCodec;
        private readonly IJsonCodec<TOutput> responseCodec;
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
            outputIsUnit = outputIsSmithyUnit || IsUnitSchema(operation.Output);
            outputSchema = operation.Output;
            requestCodec = JsonCodec.FromSchema(operation.Input);
            responseCodec = JsonCodec.FromSchema(operation.Output);
            requestTransform = SmithyRequestModifiers.Compile(operation);
            HttpErrors = CompileErrors(operation.Errors);
        }

        public IReadOnlyList<HttpOperationError> HttpErrors { get; }

        public SmithyHttpRequest SerializeRequest(TInput input)
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

        public TOutput DeserializeResponse(SmithyHttpResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            if (outputIsSmithyUnit)
            {
                return (TOutput)(object)SmithyUnit.Value;
            }

            if (outputIsUnit)
            {
                return CreateEmptyOutput(outputSchema);
            }

            if (response.Content.Length == 0)
            {
                return CreateEmptyOutput(outputSchema);
            }

            var output = responseCodec.Deserialize(response.Content);
            return output is null ? CreateEmptyOutput(outputSchema) : output;
        }

        public TInput DeserializeRequest(SmithyHttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var content = BodyBytes(request.Body);
            return content.Length == 0 ? default! : requestCodec.Deserialize(content);
        }

        public SmithyHttpResponse SerializeResponse(TOutput output) =>
            throw new NotSupportedException("AWS JSON server-side serialization is not supported.");

        public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

        public string? GetErrorDiscriminator(SmithyHttpResponse response) =>
            DeserializeErrorType(response);

        public bool RequiresErrorDiscriminator => false;

        public bool SupportsHttpStatusErrorFallback => true;

        public SmithyHttpResponse SerializeError<TError>(
            Schema<TError> errorSchema,
            TError value,
            string errorShapeId,
            int statusCode
        ) =>
            throw new NotSupportedException("AWS JSON server-side serialization is not supported.");

        private static TOutput CreateEmptyOutput(Schema<TOutput> schema)
        {
            if (schema.Resolved is IStructSchema structSchema)
            {
                return (TOutput)structSchema.BuildObject(structSchema.CreateBuilder());
            }

            return default!;
        }

        private static byte[] BodyBytes(SmithyHttpBody body) =>
            body is SmithyHttpBody.Bytes bytes ? bytes.Content : [];

        private static HttpOperationError[] CompileErrors(
            IReadOnlyList<IOperationErrorSchema> errors
        ) => errors.Select(error => (HttpOperationError)CompileError((dynamic)error)).ToArray();

        private static HttpOperationError CompileError<TError>(OperationErrorSchema<TError> error)
            where TError : Exception
        {
            var codec = JsonCodec.FromSchema(error.Schema);
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

    public static string? DeserializeErrorType(SmithyHttpResponse response)
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

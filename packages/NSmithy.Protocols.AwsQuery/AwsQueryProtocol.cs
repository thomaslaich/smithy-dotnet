using System.Text;
using System.Xml.Linq;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Protocols.AwsQuery;

public sealed class AwsQueryProtocol : QueryProtocol
{
    public AwsQueryProtocol()
        : base(ec2Query: false) { }
}

public sealed class Ec2QueryProtocol : QueryProtocol
{
    public Ec2QueryProtocol()
        : base(ec2Query: true) { }
}

public abstract class QueryProtocol(bool ec2Query) : IProtocol
{
    private const string FormContentType = "application/x-www-form-urlencoded";
    private static readonly ShapeId AwsQueryError = ShapeId.Parse("aws.protocols#awsQueryError");
    private static readonly XmlCodecFactory CodecFactory = XmlCodecFactory.Default;

    public IServiceProtocol ForService(ServiceSchema service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (string.IsNullOrWhiteSpace(service.Version))
        {
            throw new ArgumentException(
                "AWS Query protocols require a service schema with a Smithy version.",
                nameof(service)
            );
        }
        return new ServiceProtocol(
            service,
            ec2Query ? QueryProtocolKind.Ec2Query : QueryProtocolKind.AwsQuery
        );
    }

    private sealed class ServiceProtocol(ServiceSchema service, QueryProtocolKind kind)
        : IServiceProtocol
    {
        public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        )
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new OperationProtocol<TInput, TOutput>(service, operation, kind);
        }

        public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
            OperationSchema<TInput, TOutput> operation
        ) =>
            throw new NotSupportedException(
                "AWS Query protocols do not support serving operations."
            );
    }

    private sealed class OperationProtocol<TInput, TOutput>
        : IClientOperationProtocol<TInput, TOutput>
    {
        private readonly string action;
        private readonly string version;
        private readonly Schema<TInput> inputSchema;
        private readonly Schema<TOutput> outputSchema;
        private readonly ICodec<TOutput> responseCodec;
        private readonly bool outputIsSmithyUnit;
        private readonly bool outputIsUnit;
        private readonly QueryProtocolKind kind;
        private readonly Action<SmithyHttpRequest>? requestTransform;
        private readonly QueryError[] errors;

        public OperationProtocol(
            ServiceSchema service,
            OperationSchema<TInput, TOutput> operation,
            QueryProtocolKind kind
        )
        {
            action = operation.Id.Name;
            version = service.Version;
            inputSchema = operation.Input;
            outputSchema = operation.Output;
            responseCodec = CodecFactory.FromSchema(operation.Output);
            outputIsSmithyUnit = typeof(TOutput) == typeof(SmithyUnit);
            outputIsUnit = outputIsSmithyUnit || Schemas.IsSyntheticUnit(operation.Output);
            this.kind = kind;
            requestTransform = SmithyRequestModifiers.Compile(operation);
            errors = CompileErrors(operation.Errors);
        }

        public SmithyHttpRequest SerializeRequest(
            TInput input,
            CancellationToken cancellationToken = default
        )
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, "/")
            {
                Body = new SmithyHttpBody.Bytes(
                    new QueryFormSerializer(kind).Serialize(action, version, inputSchema, input)
                ),
                ContentType = FormContentType,
            };
            request.Headers["Accept"] = ["text/xml"];
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
            if (response.Content.Length == 0 || outputIsUnit)
            {
                return ValueTask.FromResult(CreateEmptyOutput(outputSchema));
            }

            var payload = ExtractResultPayload(response.Content, action, kind);
            if (payload.Length == 0)
            {
                return ValueTask.FromResult(CreateEmptyOutput(outputSchema));
            }
            return ValueTask.FromResult(responseCodec.Deserialize(payload));
        }

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(response);
            if (errors.Length == 0 || response.Content.Length == 0)
            {
                return ValueTask.FromResult<Exception?>(null);
            }

            var errorElement = ExtractErrorElement(response.Content, kind);
            var code = Child(errorElement, "Code")?.Value;
            var error = code is null
                ? null
                : errors.FirstOrDefault(candidate =>
                    string.Equals(candidate.Code, code, StringComparison.Ordinal)
                    || string.Equals(candidate.Id.Name, code, StringComparison.Ordinal)
                    || string.Equals(candidate.Id.ToString(), code, StringComparison.Ordinal)
                );
            error ??= errors.FirstOrDefault(candidate =>
                candidate.HttpStatusCode == (int)response.StatusCode
            );
            error ??= errors[0];
            return ValueTask.FromResult<Exception?>(error.Deserialize(errorElement));
        }

        private static QueryError[] CompileErrors(IReadOnlyList<IOperationErrorSchema> schemas) =>
            schemas.Select(error => (QueryError)CompileError((dynamic)error)).ToArray();

        private static QueryError CompileError<TError>(OperationErrorSchema<TError> error)
            where TError : Exception
        {
            var codec = CodecFactory.FromSchema(error.Schema);
            var code = QueryErrorCode(error.Schema) ?? error.Id.Name;
            return new QueryError(
                error.Id,
                code,
                error.HttpStatusCode,
                element => codec.Deserialize(ElementBytes(element))
            );
        }

        private static string? QueryErrorCode(Schema schema)
        {
            if (schema.GetTrait(AwsQueryError)?.Value is not { Kind: DocumentKind.Object } value)
            {
                return null;
            }
            return
                value.AsObject().TryGetValue("code", out var code)
                && code.Kind == DocumentKind.String
                ? code.AsString()
                : null;
        }
    }

    private sealed record QueryError(
        ShapeId Id,
        string Code,
        int HttpStatusCode,
        Func<XElement, Exception> Deserialize
    );

    private static byte[] ExtractResultPayload(
        byte[] content,
        string operationName,
        QueryProtocolKind kind
    )
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(content));
        if (kind == QueryProtocolKind.Ec2Query)
        {
            return ElementBytes(root);
        }

        var result = Child(root, operationName + "Result");
        return result is null ? [] : ElementBytes(result);
    }

    private static XElement ExtractErrorElement(byte[] content, QueryProtocolKind kind)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(content));
        var error =
            kind == QueryProtocolKind.AwsQuery
                ? Child(root, "Error")
                : Child(Child(root, "Errors"), "Error");
        return error
            ?? throw new InvalidOperationException(
                "Response body was missing its query error element."
            );
    }

    private static XElement? Child(XElement? parent, string localName) =>
        parent
            ?.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal)
            );

    private static byte[] ElementBytes(XElement element) =>
        Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting));

    private static T CreateEmptyOutput<T>(Schema<T> schema) =>
        schema.Resolved is IStructSchema<T> structure ? structure.BuildEmpty() : default!;
}

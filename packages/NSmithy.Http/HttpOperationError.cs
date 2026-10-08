using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Http;

public sealed class HttpOperationError(
    ShapeId id,
    int httpStatusCode,
    Func<SmithyHttpClientResponse, Exception> deserialize
)
{
    private readonly Func<SmithyHttpClientResponse, Exception> deserialize =
        deserialize ?? throw new ArgumentNullException(nameof(deserialize));

    public ShapeId Id { get; } = id;

    public int HttpStatusCode { get; } = httpStatusCode;

    public Exception Deserialize(SmithyHttpClientResponse response) => deserialize(response);

    /// <summary>Compiles each of an operation's modeled errors with <paramref name="reader"/>.</summary>
    public static HttpOperationError[] Compile(
        IReadOnlyList<IOperationErrorSchema> errors,
        IErrorReaderCompiler reader
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(reader);
        var compiler = new Compiler(reader);
        return [.. errors.Select(error => error.Accept(compiler))];
    }

    private sealed class Compiler(IErrorReaderCompiler reader)
        : IOperationErrorSchemaVisitor<HttpOperationError>
    {
        public HttpOperationError Visit<TError>(OperationErrorSchema<TError> error)
            where TError : Exception => new(error.Id, error.HttpStatusCode, reader.Compile(error));
    }
}

/// <summary>
/// Compiles one modeled error into the function that reads it from an error response, with the
/// error's CLR type in scope. Compilation runs once per operation; the returned function runs per
/// response.
/// </summary>
public interface IErrorReaderCompiler
{
    Func<SmithyHttpClientResponse, TError> Compile<TError>(OperationErrorSchema<TError> schema)
        where TError : Exception;
}

/// <summary>
/// Reads errors whose payload is the whole error structure in a codec's format.
/// <paramref name="payload"/> extracts that payload from the response, applying any protocol
/// framing.
/// </summary>
public sealed class CodecErrorReader(
    ICodecFactory codecs,
    Func<SmithyHttpClientResponse, byte[]> payload
) : IErrorReaderCompiler
{
    public Func<SmithyHttpClientResponse, TError> Compile<TError>(
        OperationErrorSchema<TError> schema
    )
        where TError : Exception
    {
        ArgumentNullException.ThrowIfNull(schema);
        var codec = codecs.FromSchema(schema.Schema);
        return response => codec.Deserialize(payload(response));
    }
}

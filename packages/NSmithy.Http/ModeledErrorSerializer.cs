using NSmithy.Core.Serde;

namespace NSmithy.Http;

/// <summary>
/// Matches a thrown exception against an operation's modeled errors and serializes it to a
/// <see cref="SmithyHttpServerResponse"/>. Built once per operation from the operation schema, so a
/// protocol's <c>TrySerializeError</c> is entirely schema-driven — the shape id and status code
/// come from each <see cref="OperationErrorSchema{TError}"/>, never from generated literals.
/// </summary>
public sealed class ModeledErrorSerializer
{
    private readonly (Type ClrType, Func<Exception, SmithyHttpServerResponse> Serialize)[] handlers;

    private ModeledErrorSerializer(
        (Type ClrType, Func<Exception, SmithyHttpServerResponse> Serialize)[] handlers
    )
    {
        this.handlers = handlers;
    }

    /// <summary>A serializer for an operation that serializes no modeled errors.</summary>
    public static ModeledErrorSerializer None { get; } = new([]);

    /// <summary>Compiles each of an operation's modeled errors with <paramref name="writer"/>.</summary>
    public static ModeledErrorSerializer Compile(
        IReadOnlyList<IOperationErrorSchema> errors,
        IErrorWriterCompiler writer
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(writer);
        var compiler = new Compiler(writer);
        return new ModeledErrorSerializer([.. errors.Select(error => error.Accept(compiler))]);
    }

    /// <summary>
    /// Serializes <paramref name="exception"/> when it is one of the operation's modeled errors.
    /// Returns false otherwise, leaving the runtime to rethrow (surfaced as a 500 by the host).
    /// </summary>
    public bool TrySerialize(Exception exception, out SmithyHttpServerResponse response)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var (clrType, serialize) in handlers)
        {
            if (clrType.IsInstanceOfType(exception))
            {
                response = serialize(exception);
                return true;
            }
        }

        response = null!;
        return false;
    }

    private sealed class Compiler(IErrorWriterCompiler writer)
        : IOperationErrorSchemaVisitor<(Type, Func<Exception, SmithyHttpServerResponse>)>
    {
        public (Type, Func<Exception, SmithyHttpServerResponse>) Visit<TError>(
            OperationErrorSchema<TError> error
        )
            where TError : Exception
        {
            var write = writer.Compile(error);
            return (typeof(TError), exception => write((TError)exception));
        }
    }
}

/// <summary>
/// Compiles one modeled error into the function that writes it as an error response, with the
/// error's CLR type in scope. Compilation runs once per operation; the returned function runs per
/// response.
/// </summary>
public interface IErrorWriterCompiler
{
    Func<TError, SmithyHttpServerResponse> Compile<TError>(OperationErrorSchema<TError> schema)
        where TError : Exception;
}

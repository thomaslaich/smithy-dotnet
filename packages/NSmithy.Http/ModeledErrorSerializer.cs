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

    /// <summary>
    /// Compiles the operation's modeled errors into a matcher. <paramref name="compile"/> maps one
    /// modeled error to its CLR exception type and a serializer; a protocol can use
    /// <see cref="IOperationErrorSchema.Accept{TResult}(IOperationErrorSchemaVisitor{TResult})"/> to
    /// recover that type without reflection or the runtime binder.
    /// </summary>
    public static ModeledErrorSerializer Compile(
        IReadOnlyList<IOperationErrorSchema> errors,
        Func<
            IOperationErrorSchema,
            (Type ClrType, Func<Exception, SmithyHttpServerResponse> Serialize)
        > compile
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(compile);
        return new ModeledErrorSerializer([.. errors.Select(compile)]);
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
}

using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Server;

/// <summary>
/// The service and operation a call is for, as telemetry names them, and how its errors count.
/// An operation can belong to more than one service, so a catalog makes one per operation.
/// </summary>
internal sealed class ServerOperationIdentity
{
    // Each modeled error's type, and whether it is a server fault rather than the caller's mistake.
    private readonly (Type Type, bool Fault)[] errors;

    internal ServerOperationIdentity(ServiceSchema service, IOperationSchema operation)
    {
        Service = service.Id;
        Operation = operation.Id;
        ServiceTag = service.Id.ToString();
        ActivityName = $"{service.Id.Name}.{operation.Id.Name}";
        errors = [.. service.ErrorsOf(operation).Select(error => error.Accept(ErrorKind.Instance))];
    }

    internal ShapeId Service { get; }

    internal ShapeId Operation { get; }

    // Constant per operation, so built once rather than on every call.
    internal string ServiceTag { get; }

    internal string ActivityName { get; }

    /// <summary>
    /// Whether <paramref name="exception"/> is the server's failure: a modeled server error, or
    /// anything unmodeled. Decided by the model, not the response, which some protocols, such as
    /// gRPC, send with a success status.
    /// </summary>
    internal bool IsFault(Exception exception)
    {
        foreach (var (type, fault) in errors)
        {
            if (type.IsInstanceOfType(exception))
            {
                return fault;
            }
        }

        return true;
    }

    private sealed class ErrorKind : IOperationErrorSchemaVisitor<(Type, bool)>
    {
        public static ErrorKind Instance { get; } = new();

        public (Type, bool) Visit<TError>(OperationErrorSchema<TError> schema)
            where TError : Exception => (typeof(TError), schema.HttpStatusCode >= 500);
    }
}

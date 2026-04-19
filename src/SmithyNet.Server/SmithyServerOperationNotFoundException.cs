using System.Globalization;

namespace SmithyNet.Server;

public sealed class SmithyServerOperationNotFoundException(string serviceName, string operationName)
    : SmithyServerException(
        string.Create(
            CultureInfo.InvariantCulture,
            $"No Smithy server operation handler is registered for '{serviceName}#{operationName}'."
        )
    )
{
    public string ServiceName { get; } = serviceName;

    public string OperationName { get; } = operationName;
}

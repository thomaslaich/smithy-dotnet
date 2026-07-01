using NSmithy.Core.Serde;

namespace NSmithy.Http;

internal static class OperationProtocolErrors
{
    public static Exception? DeserializeModeledError<TInput, TOutput>(
        IOperationProtocol<TInput, TOutput> protocol,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(response);

        var errors = protocol.ModeledErrors;
        if (errors.Count == 0)
        {
            return null;
        }

        var errorType = protocol.GetErrorDiscriminator(response);
        if (errorType is null && protocol.RequiresErrorDiscriminator)
        {
            return null;
        }

        if (errorType is not null)
        {
            var matched = errors.FirstOrDefault(error =>
                string.Equals(errorType, error.Id.Name, StringComparison.Ordinal)
                || string.Equals(errorType, error.Id.ToString(), StringComparison.Ordinal)
            );
            if (matched is not null)
            {
                return DeserializeError(protocol, matched, response);
            }
        }

        if (protocol.SupportsHttpStatusErrorFallback)
        {
            var statusMatched = errors.FirstOrDefault(error =>
                error.HttpStatusCode == (int)response.StatusCode
            );
            if (statusMatched is not null)
            {
                return DeserializeError(protocol, statusMatched, response);
            }
        }

        var fallback = errors[0];
        return protocol.SupportsHttpStatusErrorFallback && response.Content.Length == 0
            ? null
            : DeserializeError(protocol, fallback, response);
    }

    private static Exception DeserializeError<TInput, TOutput>(
        IOperationProtocol<TInput, TOutput> protocol,
        IOperationErrorSchema error,
        SmithyHttpResponse response
    ) => DeserializeError(protocol, (dynamic)error, response);

    private static Exception DeserializeError<TInput, TOutput, TError>(
        IOperationProtocol<TInput, TOutput> protocol,
        OperationErrorSchema<TError> error,
        SmithyHttpResponse response
    )
        where TError : Exception => protocol.DeserializeError(error.Schema, response);
}

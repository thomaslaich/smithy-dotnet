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

        return DeserializeModeledError(protocol, protocol.HttpErrors, response);
    }

    public static Exception? DeserializeModeledError<TInput, TOutput>(
        IOperationProtocol<TInput, TOutput> protocol,
        IReadOnlyList<HttpOperationError> errors,
        SmithyHttpResponse response
    )
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(response);

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
                return matched.Deserialize(response);
            }
        }

        if (protocol.SupportsHttpStatusErrorFallback)
        {
            var statusMatched = errors.FirstOrDefault(error =>
                error.HttpStatusCode == (int)response.StatusCode
            );
            if (statusMatched is not null)
            {
                return statusMatched.Deserialize(response);
            }
        }

        var fallback = errors[0];
        return protocol.SupportsHttpStatusErrorFallback && response.Content.Length == 0
            ? null
            : fallback.Deserialize(response);
    }
}

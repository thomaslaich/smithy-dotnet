namespace NSmithy.Http;

/// <summary>
/// The shared modeled-error resolver protocols compose into their
/// <c>DeserializeErrorAsync</c>. Discrimination rules are explicit parameters — each protocol
/// passes its own discriminator and fallback behavior instead of exposing them as public
/// interface members.
/// </summary>
public static class OperationProtocolErrors
{
    /// <param name="errors">The operation's compiled error deserializers.</param>
    /// <param name="response">The error response.</param>
    /// <param name="errorDiscriminator">Extracts the error type discriminator, or null.</param>
    /// <param name="requiresErrorDiscriminator">
    /// True for rpc-style protocols (rpcv2Cbor, gRPC) whose errors always carry an explicit
    /// discriminator: a response without one carries no modeled error.
    /// </param>
    /// <param name="supportsHttpStatusErrorFallback">
    /// True for REST protocols, which can still resolve an error from the HTTP status code when
    /// the discriminator did not resolve; false for rpc-style protocols where the HTTP status
    /// does not map to an error shape (gRPC always returns HTTP 200).
    /// </param>
    public static Exception? DeserializeModeledError(
        IReadOnlyList<HttpOperationError> errors,
        SmithyHttpResponse response,
        Func<SmithyHttpResponse, string?> errorDiscriminator,
        bool requiresErrorDiscriminator,
        bool supportsHttpStatusErrorFallback
    )
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(errorDiscriminator);

        if (errors.Count == 0)
        {
            return null;
        }

        var errorType = errorDiscriminator(response);
        if (errorType is null && requiresErrorDiscriminator)
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

        if (supportsHttpStatusErrorFallback)
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
        return supportsHttpStatusErrorFallback && response.Content.Length == 0
            ? null
            : fallback.Deserialize(response);
    }
}

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
        SmithyHttpClientResponse response,
        Func<SmithyHttpClientResponse, string?> errorDiscriminator,
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
            // A response that names an error the operation does not model is not one of its
            // modeled errors, whatever its status code.
            return matched?.Deserialize(response);
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

    /// <summary>
    /// The error name <paramref name="errorDiscriminator"/> reads from <paramref name="response"/>,
    /// without a namespace (<c>ns#Name</c>) or a trailing <c>:</c> suffix. Null when the response
    /// names no error or its body cannot be read.
    /// </summary>
    public static string? ReadErrorName(
        SmithyHttpClientResponse response,
        Func<SmithyHttpClientResponse, string?> errorDiscriminator
    )
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(errorDiscriminator);

        string? errorType;
        try
        {
            errorType = errorDiscriminator(response);
        }
        catch (Exception exception)
            when (exception
                    is FormatException
                        or InvalidOperationException
                        or System.Xml.XmlException
                        or System.Text.Json.JsonException
            )
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(errorType))
        {
            return null;
        }

        var colon = errorType.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            errorType = errorType[..colon];
        }

        var hash = errorType.LastIndexOf('#');
        return hash >= 0 ? errorType[(hash + 1)..] : errorType;
    }
}

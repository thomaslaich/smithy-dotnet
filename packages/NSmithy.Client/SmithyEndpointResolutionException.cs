namespace NSmithy.Client;

/// <summary>
/// Endpoint rules rejected the client's parameters: an error rule matched (its message is this
/// exception's message), or no rule matched at all.
/// </summary>
public sealed class SmithyEndpointResolutionException : Exception
{
    public SmithyEndpointResolutionException() { }

    public SmithyEndpointResolutionException(string message)
        : base(message) { }

    public SmithyEndpointResolutionException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>The exception for a resolution that reached no endpoint or error rule.</summary>
    public static SmithyEndpointResolutionException NoMatch() =>
        new("No endpoint rule matched the endpoint parameters.");
}

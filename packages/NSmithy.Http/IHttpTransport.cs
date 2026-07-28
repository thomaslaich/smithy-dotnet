namespace NSmithy.Http;

public enum SmithyHttpClientResponseMode
{
    Buffer,
    Stream,
}

public interface IHttpTransport
{
    Task<SmithyHttpClientResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpClientResponseMode responseMode,
        CancellationToken cancellationToken = default
    );
}

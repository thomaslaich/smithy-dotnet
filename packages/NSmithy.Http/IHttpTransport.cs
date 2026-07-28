namespace NSmithy.Http;

public enum SmithyHttpResponseMode
{
    Buffer,
    Stream,
}

public interface IHttpTransport
{
    Task<SmithyHttpResponse> SendAsync(
        SmithyHttpRequest request,
        SmithyHttpResponseMode responseMode,
        CancellationToken cancellationToken = default
    );
}

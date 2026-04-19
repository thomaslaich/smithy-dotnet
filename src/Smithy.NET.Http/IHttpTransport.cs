namespace Smithy.NET.Http;

public interface IHttpTransport
{
    Task<SmithyHttpResponse> SendAsync(
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    );
}

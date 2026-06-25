namespace NSmithy.Http;

public interface IEventStreamHttpTransport
{
    Task<SmithyEventStreamHttpResponse> SendAsync(
        SmithyEventStreamHttpRequest request,
        CancellationToken cancellationToken = default
    );
}

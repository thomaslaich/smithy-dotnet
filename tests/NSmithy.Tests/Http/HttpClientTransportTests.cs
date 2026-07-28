using System.Net;
using NSmithy.Http;

namespace NSmithy.Tests.Http;

public sealed class HttpClientTransportTests
{
    [Fact]
    public async Task SendAsyncSendsRequestUriAsProvided()
    {
        using var httpClient = new HttpClient(new Handler());
        var transport = new HttpClientTransport(httpClient);

        await transport.SendAsync(
            new SmithyHttpRequest(
                HttpMethod.Get,
                "https://example.test/base/forecast?units=metric"
            ),
            SmithyHttpResponseMode.Buffer
        );
    }

    [Fact]
    public async Task SendAsyncExposesTrailingHeadersThroughTrailerAccessor()
    {
        using var httpClient = new HttpClient(new TrailerHandler());
        var transport = new HttpClientTransport(httpClient);

        var response = await transport.SendAsync(
            new SmithyHttpRequest(
                HttpMethod.Post,
                "https://example.test/example.greeter.Greeter/SayHello"
            ),
            SmithyHttpResponseMode.Buffer
        );

        // grpc-status arrives as an HTTP/2 trailer; the transport keeps it out of headers.
        Assert.False(response.Headers.ContainsKey("grpc-status"));
        Assert.Equal("0", response.Trailer?.Invoke("grpc-status"));
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Assert.Equal(
                "https://example.test/base/forecast?units=metric",
                request.RequestUri?.ToString()
            );
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class TrailerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
            response.TrailingHeaders.TryAddWithoutValidation("grpc-status", "0");
            return Task.FromResult(response);
        }
    }
}

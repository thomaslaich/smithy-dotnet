using System.Net;
using NSmithy.Http;

namespace NSmithy.Tests.Http;

public sealed class HttpClientTransportTests
{
    [Fact]
    public async Task SendAsyncResolvesRelativeRequestAgainstConfiguredEndpoint()
    {
        using var httpClient = new HttpClient(new Handler())
        {
            BaseAddress = new Uri("https://ignored.example"),
        };
        var transport = new HttpClientTransport(httpClient, new Uri("https://example.test/base"));

        await transport.SendAsync(new SmithyHttpRequest(HttpMethod.Get, "/forecast?units=metric"));
    }

    [Fact]
    public async Task SendAsyncFoldsTrailingHeadersIntoResponseHeaders()
    {
        using var httpClient = new HttpClient(new TrailerHandler());
        var transport = new HttpClientTransport(httpClient, new Uri("https://example.test"));

        var response = await transport.SendAsync(
            new SmithyHttpRequest(HttpMethod.Post, "/example.greeter.Greeter/SayHello")
        );

        // grpc-status arrives as an HTTP/2 trailer; the transport surfaces it as a header.
        Assert.True(response.Headers.TryGetValue("grpc-status", out var status));
        Assert.Equal("0", status[0]);
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

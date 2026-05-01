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
}

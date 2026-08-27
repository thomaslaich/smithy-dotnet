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
            SmithyHttpClientResponseMode.Buffer
        );
    }

    [Fact]
    public async Task SendAsyncPreservesDotSegmentsInRequestPath()
    {
        using var httpClient = new HttpClient(new DotSegmentHandler());
        var transport = new HttpClientTransport(httpClient);

        await transport.SendAsync(
            new SmithyHttpRequest(HttpMethod.Get, "https://example.test/objects/foo/../key.txt"),
            SmithyHttpClientResponseMode.Buffer
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
            SmithyHttpClientResponseMode.Buffer
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

    private sealed class DotSegmentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Assert.Equal("/objects/foo/../key.txt", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task BufferedTrailerSurvivesResponseDisposal()
    {
        // The transport disposes the underlying HttpResponseMessage before returning a buffered
        // response, so the Trailer accessor must read from an eagerly captured copy — not the live
        // (disposed) message. The handler clears its trailing headers on dispose to make a
        // read-from-disposed-message regression observable: without the eager capture this returns
        // null instead of the trailer value.
        var handler = new DisposeClearingTrailerHandler();
        using var httpClient = new HttpClient(handler);
        var transport = new HttpClientTransport(httpClient);

        var response = await transport.SendAsync(
            new SmithyHttpRequest(
                HttpMethod.Post,
                "https://example.test/example.greeter.Greeter/SayHello"
            ),
            SmithyHttpClientResponseMode.Buffer
        );

        // The transport must have disposed the underlying message (otherwise this test proves
        // nothing), yet the trailer must still read from the eager capture.
        Assert.True(handler.ResponseDisposed);
        Assert.Equal("13", response.Trailer?.Invoke("grpc-status"));
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

    private sealed class DisposeClearingTrailerHandler : HttpMessageHandler
    {
        public bool ResponseDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var response = new DisposeClearingResponse(() => ResponseDisposed = true)
            {
                Content = new ByteArrayContent([]),
            };
            response.TrailingHeaders.TryAddWithoutValidation("grpc-status", "13");
            return Task.FromResult<HttpResponseMessage>(response);
        }

        // Drops its trailing headers on dispose, standing in for an HTTP stack that does not keep
        // trailers readable after the message is disposed.
        private sealed class DisposeClearingResponse(Action onDispose) : HttpResponseMessage
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    TrailingHeaders.Clear();
                    onDispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}

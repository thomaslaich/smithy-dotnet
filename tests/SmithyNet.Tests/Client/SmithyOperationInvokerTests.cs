using System.Text;
using System.Net;
using SmithyNet.Client;
using SmithyNet.Http;

namespace SmithyNet.Tests.Client;

public sealed class SmithyOperationInvokerTests
{
    [Fact]
    public async Task InvokeAsyncRunsMiddlewareBeforeTransport()
    {
        var transport = new RecordingTransport(
            new SmithyHttpResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("""{"ok":true}"""),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var middleware = new HeaderMiddleware();
        var invoker = new SmithyOperationInvoker(transport, [middleware]);

        var response = await invoker.InvokeAsync(
            "Weather",
            "GetForecast",
            new SmithyHttpRequest(HttpMethod.Get, "/forecast")
        );

        Assert.Equal("""{"ok":true}""", response.ContentText);
        Assert.True(middleware.WasCalled);
        Assert.Equal(["middleware"], transport.Request.Headers["x-smithy-test"]);
    }

    [Fact]
    public async Task InvokeAsyncThrowsDeserializedErrorForNonSuccessResponse()
    {
        var transport = new RecordingTransport(
            new SmithyHttpResponse(
                HttpStatusCode.BadRequest,
                "Bad Request",
                Encoding.UTF8.GetBytes("""{"message":"bad city"}"""),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var invoker = new SmithyOperationInvoker(transport);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.InvokeAsync(
                "Weather",
                "GetForecast",
                new SmithyHttpRequest(HttpMethod.Get, "/forecast"),
                static (response, _) =>
                    ValueTask.FromResult<Exception?>(
                        new InvalidOperationException(response.ContentText)
                    )
            )
        );

        Assert.Equal("""{"message":"bad city"}""", error.Message);
    }

    [Fact]
    public async Task InvokeAsyncThrowsGenericClientExceptionWhenErrorCannotBeDeserialized()
    {
        var transport = new RecordingTransport(
            new SmithyHttpResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var invoker = new SmithyOperationInvoker(transport);

        var error = await Assert.ThrowsAsync<SmithyClientException>(() =>
            invoker.InvokeAsync(
                "Weather",
                "GetForecast",
                new SmithyHttpRequest(HttpMethod.Get, "/forecast")
            )
        );

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task InvokeAsyncCanRetryTransientResponsesWithMiddleware()
    {
        var transport = new SequenceTransport(
            new SmithyHttpResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("""{"ok":true}"""),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var invoker = new SmithyOperationInvoker(
            transport,
            [new SmithyRetryMiddleware(maxAttempts: 2)]
        );

        var response = await invoker.InvokeAsync(
            "Weather",
            "GetForecast",
            new SmithyHttpRequest(HttpMethod.Get, "/forecast")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, transport.Attempts);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private sealed class HeaderMiddleware : ISmithyClientMiddleware
    {
        public bool WasCalled { get; private set; }

        public Task<SmithyOperationResponse> InvokeAsync(
            SmithyOperationRequest request,
            SmithyOperationNext nextOperation,
            CancellationToken cancellationToken = default
        )
        {
            WasCalled = true;
            request.Request.Headers["x-smithy-test"] = ["middleware"];
            return nextOperation(request, cancellationToken);
        }
    }

    private sealed class RecordingTransport(SmithyHttpResponse response) : IHttpTransport
    {
        public SmithyHttpRequest Request { get; private set; } = null!;

        public Task<SmithyHttpResponse> SendAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceTransport(params SmithyHttpResponse[] responses) : IHttpTransport
    {
        public int Attempts { get; private set; }

        public Task<SmithyHttpResponse> SendAsync(
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var index = Math.Min(Attempts, responses.Length - 1);
            Attempts++;
            return Task.FromResult(responses[index]);
        }
    }
}

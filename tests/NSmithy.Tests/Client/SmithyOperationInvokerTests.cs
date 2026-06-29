using System.Net;
using System.Text;
using NSmithy.Client;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

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

    [Fact]
    public async Task RuntimeRunsInterceptorsAroundTypedUnaryExecution()
    {
        List<string> calls = [];
        var interceptor = new RecordingInterceptor("one", calls);
        var transport = new RecordingTransport(
            new SmithyHttpResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport, [interceptor]);

        var output = await runtime.InvokeAsync(
            "Weather",
            "GetForecast",
            new TextProtocol(),
            "input",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal("output", output);
        Assert.Equal(
            [
                "one:before-execution:Weather.GetForecast",
                "one:before-serialization:input",
                "one:before-signing:/input",
                "one:before-transmit:/input",
                "one:after-transmit:OK",
                "one:after-deserialization:output",
                "one:after-execution",
            ],
            calls
        );
        Assert.Equal("/input", transport.Request.RequestUri);
        Assert.Equal(["signed"], transport.Request.Headers["x-smithy-test"]);
    }

    [Fact]
    public async Task RuntimeRunsAfterInterceptorsInReverseOrder()
    {
        List<string> calls = [];
        var transport = new RecordingTransport(
            new SmithyHttpResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            [new RecordingInterceptor("one", calls), new RecordingInterceptor("two", calls)]
        );

        await runtime.InvokeAsync(
            "Weather",
            "GetForecast",
            new TextProtocol(),
            "input",
            cancellationToken: CancellationToken.None
        );

        Assert.Equal(
            [
                "one:before-execution:Weather.GetForecast",
                "two:before-execution:Weather.GetForecast",
                "one:before-serialization:input",
                "two:before-serialization:input",
                "one:before-signing:/input",
                "two:before-signing:/input",
                "one:before-transmit:/input",
                "two:before-transmit:/input",
                "two:after-transmit:OK",
                "one:after-transmit:OK",
                "two:after-deserialization:output",
                "one:after-deserialization:output",
                "two:after-execution",
                "one:after-execution",
            ],
            calls
        );
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

    private sealed class RecordingInterceptor(string name, List<string> calls) : IClientInterceptor
    {
        public void OnBeforeExecution(SmithyContext context)
        {
            calls.Add(
                $"{name}:before-execution:{context.Get(SmithyContextKeys.ServiceName)}.{context.Get(SmithyContextKeys.OperationName)}"
            );
        }

        public void OnBeforeSerialization(SmithyContext context, object? input)
        {
            calls.Add($"{name}:before-serialization:{input}");
        }

        public ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            calls.Add($"{name}:before-signing:{request.RequestUri}");
            request.Headers["x-smithy-test"] = ["signed"];
            return ValueTask.FromResult(request);
        }

        public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            calls.Add($"{name}:before-transmit:{request.RequestUri}");
            return ValueTask.FromResult(request);
        }

        public void OnAfterTransmit(SmithyContext context, SmithyHttpResponse response)
        {
            calls.Add($"{name}:after-transmit:{response.ReasonPhrase}");
        }

        public void OnAfterDeserialization(SmithyContext context, object? output)
        {
            calls.Add($"{name}:after-deserialization:{output}");
        }

        public void OnAfterExecution(SmithyContext context)
        {
            calls.Add($"{name}:after-execution");
        }
    }

    private sealed class TextProtocol : IOperationProtocol<string, string>
    {
        public SmithyHttpRequest SerializeRequest(string input) =>
            new(HttpMethod.Post, $"/{input}");

        public string DeserializeResponse(SmithyHttpResponse response) => "output";

        public string DeserializeRequest(SmithyHttpRequest request) => request.RequestUri;

        public SmithyHttpResponse SerializeResponse(string output) =>
            new(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes(output),
                EmptyHeaders,
                EmptyHeaders
            );

        public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

        public string? GetErrorDiscriminator(SmithyHttpResponse response) => null;

        public bool RequiresErrorDiscriminator => false;

        public bool SupportsHttpStatusErrorFallback => true;

        public TError DeserializeError<TError>(
            Schema<TError> errorSchema,
            SmithyHttpResponse response
        ) => throw new NotSupportedException();

        public SmithyHttpResponse SerializeError<TError>(
            Schema<TError> errorSchema,
            TError value,
            string errorShapeId,
            int statusCode
        ) => throw new NotSupportedException();
    }
}

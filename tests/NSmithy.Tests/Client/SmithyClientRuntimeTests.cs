using System.Net;
using System.Text;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class SmithyClientRuntimeTests
{
    private static readonly ShapeId LengthTrait = new("smithy.api", "length");

    [Fact]
    public async Task InvokeAsyncThrowsDeserializedErrorForNonSuccessResponse()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.BadRequest,
                "Bad Request",
                Encoding.UTF8.GetBytes("""{"message":"bad city"}"""),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(Binding(new ContentTextErrorProtocol()), "input")
        );

        Assert.Equal("""{"message":"bad city"}""", error.Message);
    }

    [Fact]
    public async Task InvokeAsyncValidatesInputBeforeSerialization()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(HttpStatusCode.OK, "OK", [], EmptyHeaders, EmptyHeaders)
        );
        var runtime = new SmithyClientRuntime(transport);

        var error = await Assert.ThrowsAsync<SmithyValidationException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol(), StringInputSchema()), "x")
        );

        Assert.Equal("$", error.Errors[0].Path);
        Assert.Null(transport.Request);
    }

    [Fact]
    public async Task InvokeAsyncThrowsGenericClientExceptionWhenErrorCannotBeDeserialized()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport);

        var error = await Assert.ThrowsAsync<SmithyClientException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
    }

    [Fact]
    public async Task RuntimeRunsInterceptorsAroundTypedUnaryExecution()
    {
        List<string> calls = [];
        var interceptor = new RecordingInterceptor("one", calls);
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport, [interceptor]);

        var output = await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal("output", output);
        Assert.Equal(
            [
                "one:before-execution:Weather.GetForecast",
                "one:before-serialization:input",
                "one:before-signing:/input",
                "one:before-transmit:/input",
                "one:after-transmit:OK",
                "one:after-deserialization:output",
                "one:after-execution:ok",
            ],
            calls
        );
        Assert.Equal("/input", transport.Request.RequestUri);
        Assert.Equal(["signed"], transport.Request.Headers["x-smithy-test"]);
    }

    [Fact]
    public async Task RuntimeInvokesOperationBinding()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport);

        var output = await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal("output", output);
        Assert.Equal("/input", transport.Request.RequestUri);
    }

    [Fact]
    public async Task RuntimeInvokesOutputEventStreamThroughStreamingTransport()
    {
        List<string> calls = [];
        var transport = new RecordingStreamingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Stream.Null,
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            [new RecordingInterceptor("one", calls)],
            endpoint: new Uri("https://api.example.com/base")
        );
        var binding = new SmithyOperationBinding<string, string>(
            ShapeId.Parse("example.weather#Weather"),
            ShapeId.Parse("example.weather#Watch"),
            new OutputStreamProtocol()
        );

        var output = await runtime.InvokeAsync(binding, "input");

        Assert.Equal("stream-output", output);
        Assert.Equal(
            [
                "one:before-execution:Weather.Watch",
                "one:before-serialization:input",
                "one:before-signing:https://api.example.com/base/input",
                "one:before-transmit:https://api.example.com/base/input",
                "one:after-transmit:OK",
                "one:after-deserialization:stream-output",
                "one:after-execution:ok",
            ],
            calls
        );
        Assert.Equal("https://api.example.com/base/input", transport.Request.RequestUri);
        Assert.Equal(["signed"], transport.Request.Headers["x-smithy-test"]);
        Assert.Equal(1, transport.StreamingAttempts);
    }

    [Fact]
    public async Task RuntimeExposesConstructorEndpointInContext()
    {
        List<Uri> endpoints = [];
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var endpoint = new Uri("https://api.example.com");
        var runtime = new SmithyClientRuntime(
            transport,
            [new EndpointRecordingInterceptor(endpoints)],
            endpoint: endpoint
        );

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal([endpoint], endpoints);
    }

    [Fact]
    public async Task RuntimeResolvesRelativeRequestUriAgainstEndpointBeforeTransmit()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com/base")
        );

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal("https://api.example.com/base/input", transport.Request.RequestUri);
    }

    [Fact]
    public async Task RuntimeLeavesAbsoluteRequestUriUnchanged()
    {
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com")
        );

        await runtime.InvokeAsync(Binding(new AbsoluteUriProtocol()), "input");

        Assert.Equal("https://override.example/input", transport.Request.RequestUri);
    }

    [Fact]
    public void RuntimeRejectsRelativeEndpoint()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new SmithyClientRuntime(
                new RecordingTransport(
                    new SmithyHttpClientResponse(
                        HttpStatusCode.OK,
                        "OK",
                        [],
                        EmptyHeaders,
                        EmptyHeaders
                    )
                ),
                endpoint: new Uri("/relative", UriKind.Relative)
            )
        );

        Assert.Equal("endpoint", error.ParamName);
    }

    [Fact]
    public async Task RuntimeRunsAfterInterceptorsInReverseOrder()
    {
        List<string> calls = [];
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
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

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

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
                "two:after-execution:ok",
                "one:after-execution:ok",
            ],
            calls
        );
    }

    [Fact]
    public async Task RuntimeCanRetryTransientResponses()
    {
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        var output = await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal("output", output);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task RuntimeRunsRequestInterceptorsForEachRetryAttempt()
    {
        List<int> attempts = [];
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            [new AttemptRecordingInterceptor(attempts)],
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal([1, 2], attempts);
    }

    [Fact]
    public async Task RuntimeStartsEachRetryAttemptFromSerializedRequest()
    {
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            [new QueryAppendingInterceptor()],
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal(
            ["/input?attempt=1", "/input?attempt=2"],
            transport.Requests.Select(r => r.RequestUri)
        );
    }

    [Fact]
    public async Task RuntimeDoesNotRetryStreamingRequestBodies()
    {
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );
        await Assert.ThrowsAsync<SmithyClientException>(() =>
            runtime.InvokeAsync(Binding(new StreamingRequestProtocol()), "input")
        );

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task RuntimeRetriesTransportFailures()
    {
        var transport = new FlakyTransport(
            failures: 1,
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        var output = await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.Equal("output", output);
        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task RuntimeThrowsTransportFailureWhenNoRetryStrategyIsConfigured()
    {
        var transport = new FlakyTransport(
            failures: 1,
            new SmithyHttpClientResponse(HttpStatusCode.OK, "OK", [], EmptyHeaders, EmptyHeaders)
        );
        var runtime = new SmithyClientRuntime(transport);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task RuntimeThrowsTransportFailureWhenStrategyGivesUp()
    {
        var transport = new FlakyTransport(
            failures: 3,
            new SmithyHttpClientResponse(HttpStatusCode.OK, "OK", [], EmptyHeaders, EmptyHeaders)
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal(2, transport.Attempts);
    }

    [Fact]
    public async Task RetryStrategySeesDeserializedModeledError()
    {
        List<SmithyRetryOutcome> outcomes = [];
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.BadRequest,
                "Bad Request",
                Encoding.UTF8.GetBytes("""{"message":"throttled"}"""),
                EmptyHeaders,
                EmptyHeaders
            ),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(
                maxAttempts: 2,
                shouldRetry: outcome =>
                {
                    outcomes.Add(outcome);
                    return outcome.Error is InvalidOperationException;
                }
            )
        );

        var output = await runtime.InvokeAsync(Binding(new ContentTextErrorProtocol()), "input");

        Assert.Equal("output", output);
        var outcome = Assert.Single(outcomes);
        Assert.False(outcome.IsTransportFailure);
        Assert.Equal("""{"message":"throttled"}""", outcome.Error.Message);
        Assert.Equal(HttpStatusCode.BadRequest, outcome.Response!.StatusCode);
    }

    [Fact]
    public async Task InterceptorsObserveExecutionFailure()
    {
        List<string> calls = [];
        var transport = new RecordingTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(transport, [new RecordingInterceptor("one", calls)]);

        await Assert.ThrowsAsync<SmithyClientException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal("one:after-execution:SmithyClientException", calls[^1]);
    }

    [Fact]
    public async Task RuntimeDisposesStreamingResponseBodyWhenRetrying()
    {
        var abandoned = new TrackingStream();
        var transport = new SequenceTransport(
            StreamingResponse(HttpStatusCode.InternalServerError, abandoned),
            new SmithyHttpClientResponse(
                HttpStatusCode.OK,
                "OK",
                Encoding.UTF8.GetBytes("serialized output"),
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.True(abandoned.Disposed);
    }

    [Fact]
    public async Task RuntimeDisposesStreamingResponseBodyWhenThrowing()
    {
        var abandoned = new TrackingStream();
        var transport = new RecordingTransport(
            StreamingResponse(HttpStatusCode.InternalServerError, abandoned)
        );
        var runtime = new SmithyClientRuntime(transport);

        await Assert.ThrowsAsync<SmithyClientException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.True(abandoned.Disposed);
    }

    [Fact]
    public async Task RuntimeDisposesStreamingResponseBodyWhenDeserializationFails()
    {
        var abandoned = new TrackingStream();
        var transport = new RecordingTransport(StreamingResponse(HttpStatusCode.OK, abandoned));
        var runtime = new SmithyClientRuntime(transport);

        await Assert.ThrowsAsync<FormatException>(() =>
            runtime.InvokeAsync(Binding(new ThrowingDeserializationProtocol()), "input")
        );

        Assert.True(abandoned.Disposed);
    }

    [Fact]
    public async Task RuntimeLeavesSuccessfulStreamingResponseBodyToTheCaller()
    {
        var stream = new TrackingStream();
        var transport = new RecordingTransport(StreamingResponse(HttpStatusCode.OK, stream));
        var runtime = new SmithyClientRuntime(transport);

        await runtime.InvokeAsync(Binding(new TextProtocol()), "input");

        Assert.False(stream.Disposed);
    }

    [Fact]
    public async Task OperationTimeoutThrowsTimeoutException()
    {
        List<string> calls = [];
        var runtime = new SmithyClientRuntime(
            new HangingTransport(),
            [new RecordingInterceptor("one", calls)],
            operationTimeout: TimeSpan.FromMilliseconds(50)
        );

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal("one:after-execution:TimeoutException", calls[^1]);
    }

    [Fact]
    public async Task CallerCancellationIsNotTranslatedToTimeout()
    {
        using var cts = new CancellationTokenSource();
        var runtime = new SmithyClientRuntime(
            new HangingTransport(),
            operationTimeout: TimeSpan.FromSeconds(30)
        );

        var invocation = runtime.InvokeAsync(Binding(new TextProtocol()), "input", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
    }

    [Fact]
    public async Task OperationTimeoutSpansRetryBackoff()
    {
        var transport = new SequenceTransport(
            new SmithyHttpClientResponse(
                HttpStatusCode.InternalServerError,
                "Internal Server Error",
                [],
                EmptyHeaders,
                EmptyHeaders
            )
        );
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(
                maxAttempts: 2,
                delay: TimeSpan.FromSeconds(30)
            ),
            operationTimeout: TimeSpan.FromMilliseconds(50)
        );

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runtime.InvokeAsync(Binding(new TextProtocol()), "input")
        );

        Assert.Equal(1, transport.Attempts);
    }

    private sealed class HangingTransport : IHttpTransport
    {
        public async Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static SmithyHttpClientResponse StreamingResponse(
        HttpStatusCode statusCode,
        Stream body
    ) =>
        new(
            statusCode,
            statusCode.ToString(),
            new SmithyHttpBody.Streaming(body),
            EmptyHeaders,
            EmptyHeaders
        );

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        List<T> result = [];
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static SmithyOperationBinding<string, string> Binding(
        IClientOperationProtocol<string, string> protocol,
        Schema<string>? inputSchema = null
    ) =>
        new(
            ShapeId.Parse("example.weather#Weather"),
            ShapeId.Parse("example.weather#GetForecast"),
            protocol,
            inputSchema: inputSchema
        );

    private static Schema<string> StringInputSchema() =>
        Schemas.WithTraits(
            Schemas.String,
            [
                new Trait(
                    LengthTrait,
                    Document.From(
                        new Dictionary<string, Document>(StringComparer.Ordinal)
                        {
                            ["min"] = Document.From(2),
                        }
                    )
                ),
            ]
        );

    private sealed class RecordingTransport(SmithyHttpClientResponse response) : IHttpTransport
    {
        public SmithyHttpRequest Request { get; private set; } = null!;

        public Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingStreamingTransport(SmithyHttpClientResponse response)
        : IHttpTransport
    {
        public SmithyHttpRequest Request { get; private set; } = null!;

        public int StreamingAttempts { get; private set; }

        public Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            Assert.Equal(SmithyHttpClientResponseMode.Stream, responseMode);
            StreamingAttempts++;
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FlakyTransport(int failures, SmithyHttpClientResponse response)
        : IHttpTransport
    {
        public int Attempts { get; private set; }

        public Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            Attempts++;
            return Attempts <= failures
                ? Task.FromException<SmithyHttpClientResponse>(
                    new HttpRequestException("connection reset")
                )
                : Task.FromResult(response);
        }
    }

    private sealed class SequenceTransport(params SmithyHttpClientResponse[] responses)
        : IHttpTransport
    {
        public int Attempts { get; private set; }

        public List<SmithyHttpRequest> Requests { get; } = [];

        public Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            var index = Math.Min(Attempts, responses.Length - 1);
            Attempts++;
            Requests.Add(request);
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

        public void OnAfterTransmit(SmithyContext context, SmithyHttpClientResponse response)
        {
            calls.Add($"{name}:after-transmit:{response.ReasonPhrase}");
        }

        public void OnAfterDeserialization(SmithyContext context, object? output)
        {
            calls.Add($"{name}:after-deserialization:{output}");
        }

        public void OnAfterExecution(SmithyContext context, Exception? error)
        {
            calls.Add($"{name}:after-execution:{(error is null ? "ok" : error.GetType().Name)}");
        }
    }

    private sealed class AttemptRecordingInterceptor(List<int> attempts) : IClientInterceptor
    {
        public ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            attempts.Add(context.Get(SmithyContextKeys.Attempt));
            request.Headers["x-smithy-attempt"] =
            [
                context.Get(SmithyContextKeys.Attempt).ToString(),
            ];
            return ValueTask.FromResult(request);
        }
    }

    private sealed class EndpointRecordingInterceptor(List<Uri> endpoints) : IClientInterceptor
    {
        public void OnBeforeExecution(SmithyContext context)
        {
            endpoints.Add(context.Get(SmithyContextKeys.Endpoint));
        }
    }

    private sealed class QueryAppendingInterceptor : IClientInterceptor
    {
        public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            var separator = request.RequestUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            return ValueTask.FromResult(
                new SmithyHttpRequest(
                    request.Method,
                    $"{request.RequestUri}{separator}attempt={context.Get(SmithyContextKeys.Attempt)}"
                )
            );
        }
    }

    // The runtime depends only on the client half of the protocol contract, so test protocols
    // implement IClientOperationProtocol and skip the server members entirely.
    private class TextProtocol : IClientOperationProtocol<string, string>
    {
        public virtual SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        ) => new(HttpMethod.Post, $"/{input}");

        public virtual ValueTask<string> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult("output");

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public virtual ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(null);
    }

    private sealed class OutputStreamProtocol : TextProtocol
    {
        public override SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        ) => new(HttpMethod.Post, $"/{input}") { ExpectStreamingResponse = true };

        public override ValueTask<string> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult("stream-output");
    }

    private sealed class AbsoluteUriProtocol : TextProtocol
    {
        public override SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        ) => new(HttpMethod.Post, $"https://override.example/{input}");
    }

    private sealed class ContentTextErrorProtocol : TextProtocol
    {
        public override ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(new InvalidOperationException(response.ContentText));
    }

    private sealed class StreamingRequestProtocol : TextProtocol
    {
        public override SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        ) =>
            new(HttpMethod.Post, "/upload")
            {
                Body = new SmithyHttpBody.Streaming(new MemoryStream("hello"u8.ToArray())),
            };
    }

    private sealed class ThrowingDeserializationProtocol : TextProtocol
    {
        public override ValueTask<string> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => throw new FormatException("malformed body");
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

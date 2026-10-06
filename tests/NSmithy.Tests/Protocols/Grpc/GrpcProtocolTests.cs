using System.Buffers.Binary;
using NSmithy.Codecs.Proto;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Grpc;
using Nsmithy.Tests.Grpc;

namespace NSmithy.Tests.Protocols.Grpc;

/// <summary>
/// Exercises the native gRPC protocol end to end through the shared
/// <see cref="IServiceProtocol"/>/<see cref="IOperationProtocol{TInput, TOutput}"/> contract: client
/// serialize → server deserialize → server serialize → client deserialize, plus the framing and
/// error paths. No protoc, Grpc.Tools, or Grpc.Net is involved.
/// </summary>
public sealed class GrpcProtocolTests
{
    private static IOperationProtocol<SayHelloInput, SayHelloOutput> BuildProtocol() =>
        BuildServiceProtocol().ForOperation(SayHelloSchema.Schema);

    private static IServiceProtocol BuildServiceProtocol() =>
        new GrpcProtocol().ForService(FixturesSchema.Schema);

    private static ChatEvent Message(string text) => ChatEvent.FromMessage(new Echo(text));

    /// <summary>
    /// An event stream changes how the body is framed, not what the initial request has to satisfy.
    /// Covers the output-stream shape specifically because its input is an ordinary structure: a
    /// streaming response is no reason to stop validating the request that asked for it.
    /// </summary>
    [Fact]
    public void OutputEventStreamValidatesTheRequest()
    {
        var protocol = BuildServiceProtocol().ForServerOperation(WatchConstrainedSchema.Schema);

        Assert.NotNull(protocol.InputValidator);
        var error = Assert.Single(
            protocol.InputValidator.GetErrors(new WatchConstrainedInput("x"))
        );
        Assert.Equal("/message", error.Path);
    }

    [Fact]
    public void FramesAndUnframesAMessage()
    {
        byte[] payload = [0x0A, 0x02, 0x68, 0x69];

        var frame = GrpcMessageFraming.Frame(payload);

        Assert.Equal(0, frame[0]); // uncompressed
        Assert.Equal(
            (uint)payload.Length,
            BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1, 4))
        );
        Assert.Equal(payload, GrpcMessageFraming.ReadSingle(frame));
    }

    [Fact]
    public void SerializesRequestToGrpcMethodPath()
    {
        var protocol = BuildProtocol();

        var request = protocol.SerializeRequest(new SayHelloInput("hi"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/nsmithy.tests.grpc.Fixtures/SayHello", request.RequestUri);
        Assert.Equal("application/grpc+proto", request.ContentType);
        string[] trailers = ["trailers"];
        Assert.Equal(trailers, request.Headers["te"]);
        // 5-byte frame header + proto body (0A 02 'h' 'i')
        var body = Assert.IsType<SmithyHttpBody.Bytes>(request.Body);
        Assert.Equal(GrpcMessageFraming.HeaderLength + 4, body.Content.Length);
    }

    [Fact]
    public async Task RoundTripsClientToServerToClient()
    {
        var protocol = BuildProtocol();

        // client side
        var request = protocol.SerializeRequest(new SayHelloInput("ping"));

        // server side
        var serverInput = protocol.DeserializeRequest(request);
        Assert.Equal(new SayHelloInput("ping"), serverInput);
        var serverResponse = protocol.SerializeResponse(new SayHelloOutput("pong"));
        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "0"),
            serverResponse.Trailers!(null)
        );

        // client side
        var response = await ToClientResponseAsync(serverResponse);
        Assert.False(protocol.IsErrorResponse(response));
        var clientOutput = protocol.DeserializeResponse(response);
        Assert.Equal(new SayHelloOutput("pong"), clientOutput);
    }

    [Fact]
    public async Task SerializesAndDiscriminatesModeledErrors()
    {
        var protocol = BuildProtocol();

        Assert.True(
            protocol.TrySerializeError(new ThrottlingError("slow down"), out var serverResponse)
        );
        // HTTP 429 → gRPC RESOURCE_EXHAUSTED (8)
        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "8"),
            serverResponse.Trailers!(null)
        );

        var response = await ToClientResponseAsync(serverResponse);
        Assert.True(protocol.IsErrorResponse(response));
        var error = Assert.IsType<ThrottlingError>(await protocol.DeserializeErrorAsync(response));
        Assert.Equal("slow down", error.Message);
    }

    [Fact]
    public async Task ServerStreamingSerializesUnaryRequestAndReadsEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);

        var request = protocol.SerializeRequest(new WatchInput("start"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/nsmithy.tests.grpc.Fixtures/Watch", request.RequestUri);
        Assert.Equal("application/grpc+proto", request.ContentType);
        // The response is a live event stream, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);
        Assert.Equal(
            [new WatchInput("start")],
            await DecodeChunks(BodyChunks(request.Body), WatchInputSchema.Schema)
        );

        var response = EventStreamResponse([
            ChatEventSchema.Schema.SerializeForTest(Message("one")),
            ChatEventSchema.Schema.SerializeForTest(Message("two")),
        ]);

        Assert.Equal(
            [Message("one"), Message("two")],
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events!)
        );
    }

    [Fact]
    public async Task ClientStreamingSerializesEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadInput(ToAsync([Message("one"), Message("two")]))
        );

        Assert.Equal("/nsmithy.tests.grpc.Fixtures/Upload", request.RequestUri);
        // Client streaming has a unary response, so it stays in Buffer mode.
        Assert.False(request.ExpectStreamingResponse);
        Assert.Equal(
            [Message("one"), Message("two")],
            await DecodeChunks(BodyChunks(request.Body), ChatEventSchema.Schema)
        );
    }

    [Fact]
    public async Task BidirectionalStreamingSerializesAndReadsEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(ChatSchema.Schema);

        var request = protocol.SerializeRequest(new ChatInput(ToAsync([Message("client")])));

        Assert.Equal("/nsmithy.tests.grpc.Fixtures/Chat", request.RequestUri);
        // Duplex streams the response too, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);
        Assert.Equal(
            [Message("client")],
            await DecodeChunks(BodyChunks(request.Body), ChatEventSchema.Schema)
        );

        var response = EventStreamResponse([
            ChatEventSchema.Schema.SerializeForTest(Message("server")),
        ]);

        Assert.Equal(
            [Message("server")],
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events!)
        );
    }

    [Fact]
    public void ProtoCodecSupportsEventUnionAsTopLevelMessage()
    {
        var codec = ProtoCodecFactory.Default.FromSchema(ChatEventSchema.Schema);

        var payload = codec.Serialize(Message("hello"));
        var decoded = codec.Deserialize(payload);

        var message = Assert.IsType<ChatEvent.Message>(decoded);
        Assert.Equal(new Echo("hello"), message.Value);
    }

    [Fact]
    public async Task StreamingTransportWritesAndReadsGrpcFrames()
    {
        byte[]? requestBody = null;
        using var httpClient = new HttpClient(
            new DelegateHandler(
                async (request, cancellationToken) =>
                {
                    requestBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                    var responseBody = GrpcMessageFraming.Frame(
                        EchoSchema.Schema.SerializeForTest(new Echo("response"))
                    );
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(responseBody)
                        {
                            Headers = { ContentType = new("application/grpc+proto") },
                        },
                    };
                    // A compliant gRPC server closes the stream with a grpc-status trailer; the
                    // client now requires it to distinguish success from a truncated/failed stream.
                    response.TrailingHeaders.TryAddWithoutValidation("grpc-status", "0");
                    return response;
                }
            )
        )
        {
            BaseAddress = new Uri("http://localhost"),
        };
        var transport = new HttpClientTransport(httpClient);
        var request = new SmithyHttpRequest(HttpMethod.Post, "/example.Service/Stream")
        {
            ContentType = "application/grpc+proto",
            Body = new SmithyHttpBody.EventStreaming(
                ToAsync<ReadOnlyMemory<byte>>([
                    GrpcMessageFraming.Frame(
                        EchoSchema.Schema.SerializeForTest(new Echo("request"))
                    ),
                ])
            ),
        };

        var response = await transport.SendAsync(request, SmithyHttpClientResponseMode.Stream);

        Assert.NotNull(requestBody);
        Assert.Equal(
            [new Echo("request")],
            await DecodeBody(new MemoryStream(requestBody!), EchoSchema.Schema)
        );
        var responseBody = Assert.IsType<SmithyHttpBody.Streaming>(response.Body);
        Assert.Equal(
            [new Echo("response")],
            await DecodeBody(responseBody.Content, EchoSchema.Schema)
        );
    }

    [Fact]
    public async Task StreamingClientThrowsOnNonZeroGrpcStatusTrailer()
    {
        using var httpClient = GrpcStreamClient(grpcStatus: "13", grpcMessage: "handler blew up");
        var transport = new HttpClientTransport(httpClient);
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);

        var response = await transport.SendAsync(
            StreamRequest(),
            SmithyHttpClientResponseMode.Stream
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events!)
        );
        Assert.Contains("13", ex.Message);
        Assert.Contains("Internal", ex.Message); // grpc-status 13 → Internal
        Assert.Contains("handler blew up", ex.Message);
    }

    [Fact]
    public async Task StreamingClientThrowsOnMissingGrpcStatusTrailer()
    {
        // A stream that ends with no grpc-status (a truncated or non-compliant response) must surface
        // as an error rather than a clean, successful completion.
        using var httpClient = GrpcStreamClient(grpcStatus: null);
        var transport = new HttpClientTransport(httpClient);
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);

        var response = await transport.SendAsync(
            StreamRequest(),
            SmithyHttpClientResponseMode.Stream
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events!)
        );
        Assert.Contains("without a grpc-status", ex.Message);
    }

    [Fact]
    public void StreamingDeserializeThrowsDetailedErrorOnTransportFailure()
    {
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);
        var response = new SmithyHttpClientResponse(
            System.Net.HttpStatusCode.ServiceUnavailable,
            "Service Unavailable",
            Stream.Null,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["grpc-message"] = ["upstream is down"],
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            protocol.DeserializeResponseAsync(response).AsTask().GetAwaiter().GetResult()
        );
        Assert.Contains("503", ex.Message);
        Assert.Contains("upstream is down", ex.Message);
    }

    [Fact]
    public async Task DeserializesAllDefaultMessageAsEmptyInstanceNotNull()
    {
        // An all-default message proto-encodes to zero bytes; the framed body is then a header with a
        // zero-length payload. Deserialization must yield an (empty) instance, not null.
        var protocol = BuildProtocol();
        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(new SayHelloOutput())
        );

        var output = protocol.DeserializeResponse(response);

        Assert.NotNull(output);
    }

    [Fact]
    public void ProtoCodecReturnsNullForUnrecognizedUnionCase()
    {
        // A peer (e.g. a newer Grpc.Net build) sends a union whose only field is a case number this
        // build doesn't know. Deserialization must skip it (return null) rather than throw.
        var futureBytes = FutureEvent("from the future");

        var decoded = ProtoCodecFactory
            .Default.FromSchema(ChatEventSchema.Schema)
            .Deserialize(futureBytes);

        Assert.Null(decoded);
    }

    [Fact]
    public async Task ServerStreamingSkipsUnrecognizedUnionEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);
        var response = EventStreamResponse([
            ChatEventSchema.Schema.SerializeForTest(Message("known")),
            FutureEvent("unknown"),
        ]);

        var events = await CollectAsync(
            (await protocol.DeserializeResponseAsync(response)).Events!
        );

        var only = Assert.Single(events);
        Assert.Equal(new Echo("known"), Assert.IsType<ChatEvent.Message>(only).Value);
    }

    [Fact]
    public void ServerStreamTrailersReportOkOnCleanCompletion()
    {
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);

        var response = protocol.SerializeResponse(new WatchOutput(ToAsync([Message("one")])));

        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "0"),
            response.Trailers!(null)
        );
    }

    [Fact]
    public void ServerStreamTrailersReportInternalOnMidStreamFailure()
    {
        var protocol = BuildServiceProtocol().ForOperation(WatchSchema.Schema);

        var response = protocol.SerializeResponse(new WatchOutput(ToAsync([Message("one")])));

        // A mid-stream failure the host observes maps to Internal (13) + message, instead of
        // silently truncating the stream with no status.
        var trailers = response.Trailers!(new InvalidOperationException("kaboom"));
        Assert.Contains(new KeyValuePair<string, string>("grpc-status", "13"), trailers);
        Assert.Contains(new KeyValuePair<string, string>("grpc-message", "kaboom"), trailers);
    }

    private static SmithyHttpClientResponse EventStreamResponse(IEnumerable<byte[]> payloads) =>
        new(
            System.Net.HttpStatusCode.OK,
            null,
            FramedBodyStream(payloads),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/grpc+proto"],
            },
            static name => name == "grpc-status" ? "0" : null
        );

    private static MemoryStream FramedBodyStream(IEnumerable<byte[]> payloads)
    {
        var stream = new MemoryStream();
        foreach (var payload in payloads)
        {
            var framed = GrpcMessageFraming.Frame(payload);
            stream.Write(framed, 0, framed.Length);
        }

        stream.Position = 0;
        return stream;
    }

    private static IAsyncEnumerable<ReadOnlyMemory<byte>> BodyChunks(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.EventStreaming eventStreaming => eventStreaming.Content,
            SmithyHttpBody.Bytes bytes => ToAsync<ReadOnlyMemory<byte>>([bytes.Content]),
            _ => ToAsync<ReadOnlyMemory<byte>>([]),
        };

    private static async Task<List<T>> DecodeChunks<T>(
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        Schema<T> schema
    )
    {
        var stream = new MemoryStream();
        await foreach (var chunk in chunks)
        {
            stream.Write(chunk.Span);
        }

        stream.Position = 0;
        return await DecodeBody(stream, schema);
    }

    private static async Task<List<T>> DecodeBody<T>(Stream framedBody, Schema<T> schema)
    {
        var codec = ProtoCodecFactory.Default.FromSchema(schema);
        var values = new List<T>();
        await foreach (var payload in GrpcMessageFraming.ReadAllAsync(framedBody))
        {
            values.Add(codec.Deserialize(payload));
        }

        return values;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var collected = new List<T>();
        await foreach (var value in values)
        {
            collected.Add(value);
        }

        return collected;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            await Task.CompletedTask;
            yield return value;
        }
    }

    private static byte[] FutureEvent(string text) =>
        FutureChatEventSchema.Schema.SerializeForTest(new FutureChatEvent.Future(new Echo(text)));

    private static SmithyHttpRequest StreamRequest() =>
        new(HttpMethod.Post, "/nsmithy.tests.grpc.Fixtures/Watch")
        {
            ContentType = "application/grpc+proto",
            Body = new SmithyHttpBody.EventStreaming(
                ToAsync<ReadOnlyMemory<byte>>([
                    GrpcMessageFraming.Frame(
                        WatchInputSchema.Schema.SerializeForTest(new WatchInput("req"))
                    ),
                ])
            ),
        };

    private static HttpClient GrpcStreamClient(
        string? grpcStatus = "0",
        string? grpcMessage = null
    ) =>
        new(
            new DelegateHandler(
                async (request, cancellationToken) =>
                {
                    await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                    var body = GrpcMessageFraming.Frame(
                        ChatEventSchema.Schema.SerializeForTest(Message("event"))
                    );
                    var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(body)
                        {
                            Headers = { ContentType = new("application/grpc+proto") },
                        },
                    };
                    if (grpcStatus is not null)
                    {
                        response.TrailingHeaders.TryAddWithoutValidation("grpc-status", grpcStatus);
                    }

                    if (grpcMessage is not null)
                    {
                        response.TrailingHeaders.TryAddWithoutValidation(
                            "grpc-message",
                            grpcMessage
                        );
                    }

                    return response;
                }
            )
        )
        {
            BaseAddress = new Uri("http://localhost"),
        };

    // Simulates the unary wire: the server response's body is drained to bytes and its trailers are
    // available through the response trailer accessor.
    private static async Task<SmithyHttpClientResponse> ToClientResponseAsync(
        SmithyHttpServerResponse response
    )
    {
        var buffer = new MemoryStream();
        await foreach (var chunk in response.Body)
        {
            buffer.Write(chunk.Span);
        }

        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        var contentHeaders = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var header in response.Headers)
        {
            var target = string.Equals(
                header.Key,
                "Content-Type",
                StringComparison.OrdinalIgnoreCase
            )
                ? contentHeaders
                : headers;
            target[header.Key] = header.Value;
        }

        var trailers =
            response
                .Trailers?.Invoke(null)
                .ToDictionary(
                    trailer => trailer.Key,
                    trailer => trailer.Value,
                    StringComparer.OrdinalIgnoreCase
                )
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new SmithyHttpClientResponse(
            (System.Net.HttpStatusCode)response.StatusCode,
            null,
            new SmithyHttpBody.Bytes(buffer.ToArray()),
            headers,
            contentHeaders,
            name => trailers.TryGetValue(name, out var value) ? value : null
        );
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send
    ) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }
}

file static class GrpcProtocolTestExtensions
{
    public static byte[] SerializeForTest<T>(this Schema<T> schema, T value) =>
        ProtoCodecFactory.Default.FromSchema(schema).Serialize(value);
}

using System.Buffers.Binary;
using NSmithy.Codecs.Proto;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Grpc;

namespace NSmithy.Tests.Protocols.Grpc;

/// <summary>
/// Exercises the native gRPC protocol end to end through the shared
/// <see cref="IServiceProtocol"/>/<see cref="IOperationProtocol{TInput, TOutput}"/> contract: client
/// serialize → server deserialize → server serialize → client deserialize, plus the framing and
/// error paths. No protoc, Grpc.Tools, or Grpc.Net is involved.
/// </summary>
public sealed class GrpcProtocolTests
{
    private static readonly ShapeId ProtoIndex = ShapeId.Parse("alloy.proto#protoIndex");

    private static IEnumerable<Trait> Field(int index) =>
        [new Trait(ProtoIndex, Document.From(index))];

    public sealed record Echo(string Message);

    public sealed class TestGrpcException(string? message) : Exception(message);

    public abstract record ChatEvent
    {
        private ChatEvent() { }

        public sealed record Message(Echo Value) : ChatEvent;
    }

    public sealed class EchoBuilder
    {
        public string? Message { get; set; }
    }

    public sealed record EchoEvents(IAsyncEnumerable<Echo> Events);

    public sealed class EchoEventsBuilder
    {
        public IAsyncEnumerable<Echo>? Events { get; set; }
    }

    public sealed record ChatEvents(IAsyncEnumerable<ChatEvent> Events);

    public sealed class ChatEventsBuilder
    {
        public IAsyncEnumerable<ChatEvent>? Events { get; set; }
    }

    public sealed class TestGrpcExceptionBuilder
    {
        public string? Message { get; set; }
    }

    private static Schema<Echo> EchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("message", x => x.Message, (b, v) => b.Message = v, Schemas.String, Field(1))
            .Build(() => new EchoBuilder(), b => new Echo(b.Message!));

    private static Schema<TestGrpcException> ErrorSchema(string name) =>
        Schemas
            .Structure<TestGrpcException, TestGrpcExceptionBuilder>(
                ShapeId.Parse($"example.greeter#{name}")
            )
            .Required("message", x => x.Message, (b, v) => b.Message = v, Schemas.String, Field(1))
            .Build(() => new TestGrpcExceptionBuilder(), b => new TestGrpcException(b.Message));

    private static Schema<ChatEvent> ChatEventSchema(string name) =>
        Schemas
            .Union<ChatEvent>(ShapeId.Parse($"example.greeter#{name}"))
            .Case(
                "message",
                static value => value is ChatEvent.Message,
                static value => ((ChatEvent.Message)value).Value,
                static value => new ChatEvent.Message(value!),
                EchoSchema($"{name}Message"),
                Field(1)
            )
            .Build();

    private static Schema<EchoEvents> EchoEventsSchema(string name) =>
        Schemas
            .Structure<EchoEvents, EchoEventsBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required(
                "events",
                x => x.Events,
                (b, v) => b.Events = v,
                Schemas.EventStream(EchoSchema($"{name}Event"))
            )
            .Build(() => new EchoEventsBuilder(), b => new EchoEvents(b.Events!));

    private static Schema<ChatEvents> ChatEventsSchema(string name) =>
        Schemas
            .Structure<ChatEvents, ChatEventsBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required(
                "events",
                x => x.Events,
                (b, v) => b.Events = v,
                Schemas.EventStream(ChatEventSchema($"{name}Event"))
            )
            .Build(() => new ChatEventsBuilder(), b => new ChatEvents(b.Events!));

    private static IOperationProtocol<Echo, Echo> BuildProtocol()
    {
        var service = Schemas.Service(ShapeId.Parse("example.greeter#Greeter"));
        var operation = Schemas.Operation(
            ShapeId.Parse("example.greeter#SayHello"),
            EchoSchema("SayHelloInput"),
            EchoSchema("SayHelloOutput"),
            [
                Schemas.OperationError(
                    ShapeId.Parse("example.greeter#ThrottlingError"),
                    ErrorSchema("ThrottlingError"),
                    429
                ),
            ]
        );
        return new GrpcProtocol().ForService(service).ForOperation(operation);
    }

    private static IServiceProtocol BuildEventStreamServiceProtocol() =>
        new GrpcProtocol().ForService(Schemas.Service(ShapeId.Parse("example.greeter#Greeter")));

    private static OperationSchema<Echo, Echo> EchoOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoSchema($"{name}Input"),
            EchoSchema($"{name}Output")
        );

    private static Trait Length(int min, int max) =>
        new(
            ShapeId.Parse("smithy.api#length"),
            Document.From(
                new Dictionary<string, Document>(StringComparer.Ordinal)
                {
                    ["min"] = Document.From(min),
                    ["max"] = Document.From(max),
                }
            )
        );

    private static Schema<Echo> ConstrainedEchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required(
                "message",
                x => x.Message,
                (b, v) => b.Message = v,
                Schemas.String,
                [.. Field(1), Length(2, 10)]
            )
            .Build(() => new EchoBuilder(), b => new Echo(b.Message!));

    /// <summary>
    /// An event stream changes how the body is framed, not what the initial request has to satisfy.
    /// Covers the output-stream shape specifically because its input is an ordinary structure — a
    /// streaming response is no reason to stop validating the request that asked for it.
    /// </summary>
    [Fact]
    public void OutputEventStreamValidatesTheRequest()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForServerOperation(
                Schemas.Operation(
                    ShapeId.Parse("example.greeter#WatchConstrained"),
                    ConstrainedEchoSchema("WatchConstrainedInput"),
                    EchoEventsSchema("WatchConstrainedOutput")
                )
            );

        Assert.NotNull(protocol.InputValidator);
        var error = Assert.Single(protocol.InputValidator.GetErrors(new Echo("x")));
        Assert.Equal("/message", error.Path);
    }

    private static OperationSchema<Echo, EchoEvents> OutputStreamOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoSchema($"{name}Input"),
            EchoEventsSchema($"{name}Output")
        );

    private static OperationSchema<EchoEvents, Echo> InputStreamOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoEventsSchema($"{name}Input"),
            EchoSchema($"{name}Output")
        );

    private static OperationSchema<EchoEvents, EchoEvents> DuplexStreamOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoEventsSchema($"{name}Input"),
            EchoEventsSchema($"{name}Output")
        );

    private static OperationSchema<Echo, ChatEvents> ChatOutputStreamOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoSchema($"{name}Input"),
            ChatEventsSchema($"{name}Output")
        );

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

        var request = protocol.SerializeRequest(new Echo("hi"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/example.greeter.Greeter/SayHello", request.RequestUri);
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
        var request = protocol.SerializeRequest(new Echo("ping"));

        // server side
        var serverInput = protocol.DeserializeRequest(request);
        Assert.Equal(new Echo("ping"), serverInput);
        var serverResponse = protocol.SerializeResponse(new Echo("pong"));
        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "0"),
            serverResponse.Trailers!(null)
        );

        // client side
        var response = await ToClientResponseAsync(serverResponse);
        Assert.False(protocol.IsErrorResponse(response));
        var clientOutput = protocol.DeserializeResponse(response);
        Assert.Equal(new Echo("pong"), clientOutput);
    }

    [Fact]
    public async Task SerializesAndDiscriminatesModeledErrors()
    {
        var protocol = BuildProtocol();

        Assert.True(
            protocol.TrySerializeError(new TestGrpcException("slow down"), out var serverResponse)
        );
        // HTTP 429 → gRPC RESOURCE_EXHAUSTED (8)
        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "8"),
            serverResponse.Trailers!(null)
        );

        var response = await ToClientResponseAsync(serverResponse);
        Assert.True(protocol.IsErrorResponse(response));
        var error = Assert.IsType<TestGrpcException>(
            await protocol.DeserializeErrorAsync(response)
        );
        Assert.Equal("slow down", error.Message);
    }

    [Fact]
    public async Task ServerStreamingSerializesUnaryRequestAndReadsEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );

        var request = protocol.SerializeRequest(new Echo("start"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/example.greeter.Greeter/Watch", request.RequestUri);
        Assert.Equal("application/grpc+proto", request.ContentType);
        // The response is a live event stream, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);
        Assert.Equal([new Echo("start")], await DecodeChunks(BodyChunks(request.Body)));

        var response = EventStreamResponse([
            EchoSchema("WatchOutput").SerializeForTest(new Echo("one")),
            EchoSchema("WatchOutput").SerializeForTest(new Echo("two")),
        ]);

        Assert.Equal(
            [new Echo("one"), new Echo("two")],
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events)
        );
    }

    [Fact]
    public async Task ClientStreamingSerializesEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForInputEventStreamOperation(
                InputStreamOperation("Upload"),
                EchoSchema("UploadEvent")
            );

        var request = protocol.SerializeRequest(
            new EchoEvents(ToAsync([new Echo("one"), new Echo("two")]))
        );

        Assert.Equal("/example.greeter.Greeter/Upload", request.RequestUri);
        // Client streaming has a unary response, so it stays in Buffer mode.
        Assert.False(request.ExpectStreamingResponse);
        Assert.Equal(
            [new Echo("one"), new Echo("two")],
            await DecodeChunks(BodyChunks(request.Body))
        );
    }

    [Fact]
    public async Task BidirectionalStreamingSerializesAndReadsEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForDuplexEventStreamOperation(
                DuplexStreamOperation("Chat"),
                EchoSchema("ChatInputEvent"),
                EchoSchema("ChatOutputEvent")
            );

        var request = protocol.SerializeRequest(new EchoEvents(ToAsync([new Echo("client")])));

        Assert.Equal("/example.greeter.Greeter/Chat", request.RequestUri);
        // Duplex streams the response too, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);
        Assert.Equal([new Echo("client")], await DecodeChunks(BodyChunks(request.Body)));

        var response = EventStreamResponse([
            EchoSchema("ChatOutput").SerializeForTest(new Echo("server")),
        ]);

        Assert.Equal(
            [new Echo("server")],
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events)
        );
    }

    [Fact]
    public void ProtoCodecSupportsEventUnionAsTopLevelMessage()
    {
        var codec = NSmithy.Codecs.Proto.ProtoCodecFactory.Default.FromSchema(
            ChatEventSchema("ChatEvent")
        );
        var value = new ChatEvent.Message(new Echo("hello"));

        var payload = codec.Serialize(value);
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
                        EchoSchema("TransportOutput").SerializeForTest(new Echo("response"))
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
                        EchoSchema("TransportInput").SerializeForTest(new Echo("request"))
                    ),
                ])
            ),
        };

        var response = await transport.SendAsync(request, SmithyHttpClientResponseMode.Stream);

        Assert.NotNull(requestBody);
        Assert.Equal([new Echo("request")], await DecodeBody(new MemoryStream(requestBody!)));
        var responseBody = Assert.IsType<SmithyHttpBody.Streaming>(response.Body);
        Assert.Equal([new Echo("response")], await DecodeBody(responseBody.Content));
    }

    [Fact]
    public async Task StreamingClientThrowsOnNonZeroGrpcStatusTrailer()
    {
        using var httpClient = GrpcStreamClient(grpcStatus: "13", grpcMessage: "handler blew up");
        var transport = new HttpClientTransport(httpClient);
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );

        var response = await transport.SendAsync(
            StreamRequest(),
            SmithyHttpClientResponseMode.Stream
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events)
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
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );

        var response = await transport.SendAsync(
            StreamRequest(),
            SmithyHttpClientResponseMode.Stream
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events)
        );
        Assert.Contains("without a grpc-status", ex.Message);
    }

    [Fact]
    public void StreamingDeserializeThrowsDetailedErrorOnTransportFailure()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );
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
        var response = await ToClientResponseAsync(protocol.SerializeResponse(new Echo(null!)));

        var output = protocol.DeserializeResponse(response);

        Assert.NotNull(output);
    }

    [Fact]
    public void ProtoCodecReturnsNullForUnrecognizedUnionCase()
    {
        // A peer (e.g. a newer Grpc.Net build) sends a union whose only field is a case number this
        // build doesn't know. Deserialization must skip it (return null) rather than throw.
        var futureBytes = FutureChatEventSchema()
            .SerializeForTest(new ChatEvent.Message(new Echo("from the future")));

        var decoded = ProtoCodecFactory
            .Default.FromSchema(ChatEventSchema("ChatEvent"))
            .Deserialize(futureBytes);

        Assert.Null(decoded);
    }

    [Fact]
    public async Task ServerStreamingSkipsUnrecognizedUnionEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                ChatOutputStreamOperation("Watch"),
                ChatEventSchema("WatchEvent")
            );
        var response = EventStreamResponse([
            ChatEventSchema("WatchEvent")
                .SerializeForTest(new ChatEvent.Message(new Echo("known"))),
            FutureChatEventSchema().SerializeForTest(new ChatEvent.Message(new Echo("unknown"))),
        ]);

        var events = await CollectAsync((await protocol.DeserializeResponseAsync(response)).Events);

        var only = Assert.Single(events);
        Assert.Equal(new Echo("known"), Assert.IsType<ChatEvent.Message>(only).Value);
    }

    [Fact]
    public void ServerStreamTrailersReportOkOnCleanCompletion()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );

        var response = protocol.SerializeResponse(new EchoEvents(ToAsync([new Echo("one")])));

        Assert.Contains(
            new KeyValuePair<string, string>("grpc-status", "0"),
            response.Trailers!(null)
        );
    }

    [Fact]
    public void ServerStreamTrailersReportInternalOnMidStreamFailure()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForOutputEventStreamOperation(
                OutputStreamOperation("Watch"),
                EchoSchema("WatchEvent")
            );

        var response = protocol.SerializeResponse(new EchoEvents(ToAsync([new Echo("one")])));

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

    private static async Task<List<Echo>> DecodeChunks(
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks
    )
    {
        var stream = new MemoryStream();
        await foreach (var chunk in chunks)
        {
            stream.Write(chunk.Span);
        }

        stream.Position = 0;
        return await DecodeBody(stream);
    }

    private static async Task<List<Echo>> DecodeBody(Stream framedBody)
    {
        var codec = NSmithy.Codecs.Proto.ProtoCodecFactory.Default.FromSchema(
            EchoSchema("Decoded")
        );
        var values = new List<Echo>();
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

    // A union schema that carries the ChatEvent.Message case at a proto field number (99) the normal
    // ChatEventSchema (field 1) does not recognize — stands in for a newer peer's added oneof case.
    private static Schema<ChatEvent> FutureChatEventSchema() =>
        Schemas
            .Union<ChatEvent>(ShapeId.Parse("example.greeter#FutureChatEvent"))
            .Case(
                "future",
                static value => value is ChatEvent.Message,
                static value => ((ChatEvent.Message)value).Value,
                static value => new ChatEvent.Message(value!),
                EchoSchema("FutureMessage"),
                Field(99)
            )
            .Build();

    private static SmithyHttpRequest StreamRequest() =>
        new(HttpMethod.Post, "/example.greeter.Greeter/Stream")
        {
            ContentType = "application/grpc+proto",
            Body = new SmithyHttpBody.EventStreaming(
                ToAsync<ReadOnlyMemory<byte>>([
                    GrpcMessageFraming.Frame(
                        EchoSchema("StreamInput").SerializeForTest(new Echo("req"))
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
                        EchoSchema("StreamOutput").SerializeForTest(new Echo("event"))
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
        NSmithy.Codecs.Proto.ProtoCodecFactory.Default.FromSchema(schema).Serialize(value);
}

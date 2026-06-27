using System.Buffers.Binary;
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

    public abstract record ChatEvent
    {
        private ChatEvent() { }

        public sealed record Message(Echo Value) : ChatEvent;
    }

    public sealed class EchoBuilder
    {
        public string? Message { get; set; }
    }

    private static Schema<Echo> EchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("message", x => x.Message, (b, v) => b.Message = v, Schemas.String, Field(1))
            .Build(() => new EchoBuilder(), b => new Echo(b.Message!));

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

    private static IOperationProtocol<Echo, Echo> BuildProtocol()
    {
        var service = Schemas.Service(ShapeId.Parse("example.greeter#Greeter"));
        var operation = Schemas.Operation(
            ShapeId.Parse("example.greeter#SayHello"),
            EchoSchema("SayHelloInput"),
            EchoSchema("SayHelloOutput")
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
        Assert.Equal(GrpcMessageFraming.HeaderLength + 4, request.Content!.Length);
    }

    [Fact]
    public void RoundTripsClientToServerToClient()
    {
        var protocol = BuildProtocol();

        // client side
        var request = protocol.SerializeRequest(new Echo("ping"));

        // server side
        var serverInput = protocol.DeserializeRequest(request);
        Assert.Equal(new Echo("ping"), serverInput);
        var response = protocol.SerializeResponse(new Echo("pong"));
        Assert.False(protocol.IsErrorResponse(response));
        string[] okStatus = ["0"];
        Assert.Equal(okStatus, response.Headers["grpc-status"]);

        // client side
        var clientOutput = protocol.DeserializeResponse(response);
        Assert.Equal(new Echo("pong"), clientOutput);
    }

    [Fact]
    public void SerializesAndDiscriminatesModeledErrors()
    {
        var protocol = BuildProtocol();
        var errorSchema = EchoSchema("ThrottlingError");

        var response = protocol.SerializeError(
            errorSchema,
            new Echo("slow down"),
            "example.greeter#ThrottlingError",
            429
        );

        Assert.True(protocol.IsErrorResponse(response));
        // HTTP 429 → gRPC RESOURCE_EXHAUSTED (8)
        string[] exhaustedStatus = ["8"];
        Assert.Equal(exhaustedStatus, response.Headers["grpc-status"]);
        Assert.Equal("example.greeter#ThrottlingError", protocol.GetErrorDiscriminator(response));
        Assert.Equal(new Echo("slow down"), protocol.DeserializeError(errorSchema, response));
    }

    [Fact]
    public async Task ServerStreamingSerializesUnaryRequestAndReadsEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForServerEventStreamOperation(EchoOperation("Watch"), EchoSchema("WatchEvent"));

        var request = protocol.SerializeRequest(new Echo("start"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/example.greeter.Greeter/Watch", request.RequestUri);
        Assert.Equal("application/grpc+proto", request.ContentType);
        Assert.Equal([new Echo("start")], await DecodeEvents(request.Events));

        var response = EventStreamResponse([
            new SmithyEventFrame(EchoSchema("WatchOutput").SerializeForTest(new Echo("one"))),
            new SmithyEventFrame(EchoSchema("WatchOutput").SerializeForTest(new Echo("two"))),
        ]);

        Assert.Equal(
            [new Echo("one"), new Echo("two")],
            await CollectAsync(protocol.DeserializeResponseEventsAsync(response))
        );
    }

    [Fact]
    public async Task ClientStreamingSerializesEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForClientEventStreamOperation(EchoOperation("Upload"), EchoSchema("UploadEvent"));

        var request = protocol.SerializeRequest(ToAsync([new Echo("one"), new Echo("two")]));

        Assert.Equal("/example.greeter.Greeter/Upload", request.RequestUri);
        Assert.Equal([new Echo("one"), new Echo("two")], await DecodeEvents(request.Events));
    }

    [Fact]
    public async Task BidirectionalStreamingSerializesAndReadsEvents()
    {
        var protocol = BuildEventStreamServiceProtocol()
            .ForBidirectionalEventStreamOperation(
                EchoOperation("Chat"),
                EchoSchema("ChatInputEvent"),
                EchoSchema("ChatOutputEvent")
            );

        var request = protocol.SerializeRequest(ToAsync([new Echo("client")]));

        Assert.Equal("/example.greeter.Greeter/Chat", request.RequestUri);
        Assert.Equal([new Echo("client")], await DecodeEvents(request.Events));

        var response = EventStreamResponse([
            new SmithyEventFrame(EchoSchema("ChatOutput").SerializeForTest(new Echo("server"))),
        ]);

        Assert.Equal(
            [new Echo("server")],
            await CollectAsync(protocol.DeserializeResponseEventsAsync(response))
        );
    }

    [Fact]
    public void ProtoCodecSupportsEventUnionAsTopLevelMessage()
    {
        var codec = NSmithy.Codecs.Proto.ProtoCodec.FromSchema(ChatEventSchema("ChatEvent"));
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
        var transport = new GrpcEventStreamHttpClientTransport(httpClient);
        var request = new SmithyEventStreamHttpRequest(HttpMethod.Post, "/example.Service/Stream")
        {
            ContentType = "application/grpc+proto",
            Events = ToAsync([
                new SmithyEventFrame(
                    EchoSchema("TransportInput").SerializeForTest(new Echo("request"))
                ),
            ]),
        };

        var response = await transport.SendAsync(request);

        Assert.NotNull(requestBody);
        Assert.Equal([new Echo("request")], await DecodeEvents(FramesFromBytes(requestBody!)));
        Assert.Equal([new Echo("response")], await DecodeEvents(response.Events));
    }

    private static SmithyEventStreamHttpResponse EventStreamResponse(
        IEnumerable<SmithyEventFrame> frames
    ) =>
        new(
            System.Net.HttpStatusCode.OK,
            null,
            ToAsync(frames),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/grpc+proto"],
            }
        );

    private static async Task<List<Echo>> DecodeEvents(IAsyncEnumerable<SmithyEventFrame> events)
    {
        var codec = NSmithy.Codecs.Proto.ProtoCodec.FromSchema(EchoSchema("Decoded"));
        var values = new List<Echo>();
        await foreach (var frame in events)
        {
            values.Add(codec.Deserialize(frame.Payload));
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

    private static IAsyncEnumerable<SmithyEventFrame> FramesFromBytes(byte[] body) =>
        GrpcMessageFraming.ReadAllAsync(new MemoryStream(body));

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
        NSmithy.Codecs.Proto.ProtoCodec.FromSchema(schema).Serialize(value);
}

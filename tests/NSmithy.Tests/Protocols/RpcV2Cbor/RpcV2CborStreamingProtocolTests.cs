using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
using NSmithy.Http;
using NSmithy.Protocols.RpcV2Cbor;

namespace NSmithy.Tests.Protocols.RpcV2Cbor;

public sealed class RpcV2CborStreamingProtocolTests
{
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

    public sealed class EnvelopeBuilder
    {
        public string? Name { get; set; }

        public ChatEvent? Events { get; set; }
    }

    private static IServiceProtocol BuildServiceProtocol() =>
        new RpcV2CborProtocol().ForService(
            Schemas.Service(ShapeId.Parse("example.greeter#Greeter"))
        );

    private static Schema<Echo> EchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("message", x => x.Message, (b, v) => b.Message = v, Schemas.String)
            .Build(() => new EchoBuilder(), b => new Echo(b.Message!));

    private static Schema<ChatEvent> ChatEventSchema(string name) =>
        Schemas
            .Union<ChatEvent>(ShapeId.Parse($"example.greeter#{name}"))
            .Case(
                "message",
                static value => value is ChatEvent.Message,
                static value => ((ChatEvent.Message)value).Value,
                static value => new ChatEvent.Message(value!),
                EchoSchema($"{name}Message")
            )
            .Build();

    private static Schema<EnvelopeBuilder> StreamEnvelopeSchema(string name) =>
        Schemas
            .Structure<EnvelopeBuilder, EnvelopeBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("events", x => x.Events!, (b, v) => b.Events = v, ChatEventSchema("Events"))
            .Build(() => new EnvelopeBuilder(), b => b);

    private static Schema<EnvelopeBuilder> InitialEnvelopeSchema(string name) =>
        Schemas
            .Structure<EnvelopeBuilder, EnvelopeBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("name", x => x.Name!, (b, v) => b.Name = v, Schemas.String)
            .Required("events", x => x.Events!, (b, v) => b.Events = v, ChatEventSchema("Events"))
            .Build(() => new EnvelopeBuilder(), b => b);

    private static OperationSchema<Echo, EnvelopeBuilder> OutputOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoSchema($"{name}Input"),
            StreamEnvelopeSchema($"{name}Output")
        );

    private static OperationSchema<EnvelopeBuilder, Echo> InputOperation(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            StreamEnvelopeSchema($"{name}Input"),
            EchoSchema($"{name}Output")
        );

    private static OperationSchema<Echo, EnvelopeBuilder> OutputOperationWithInitial(string name) =>
        Schemas.Operation(
            ShapeId.Parse($"example.greeter#{name}"),
            EchoSchema($"{name}Input"),
            InitialEnvelopeSchema($"{name}Output")
        );

    [Fact]
    public async Task ServerStreamingSerializesUnaryRequestAndReadsCborEventStream()
    {
        var protocol = BuildServiceProtocol()
            .ForOutputEventStreamOperation(OutputOperation("Watch"), ChatEventSchema("WatchEvent"));

        var request = protocol.SerializeRequest(new Echo("start"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/service/Greeter/operation/Watch", request.RequestUri);
        Assert.Equal("application/cbor", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(ToAsync([new ChatEvent.Message(new Echo("one"))]))
        );

        var events = await CollectAsync(protocol.DeserializeResponseEventsAsync(response));
        var message = Assert.IsType<ChatEvent.Message>(Assert.Single(events));
        Assert.Equal(new Echo("one"), message.Value);
    }

    [Fact]
    public async Task ClientStreamingSerializesEventStreamRequest()
    {
        var protocol = BuildServiceProtocol()
            .ForInputEventStreamOperation(InputOperation("Upload"), ChatEventSchema("UploadEvent"));

        var request = protocol.SerializeRequest(ToAsync([new ChatEvent.Message(new Echo("one"))]));

        Assert.Equal("/service/Greeter/operation/Upload", request.RequestUri);
        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/cbor"], request.Headers["Accept"]);

        var framed = await BodyBytesAsync(request.Body);
        var message = Assert.Single(await ReadMessagesAsync(framed));
        Assert.Equal("event", message.StringHeader(":message-type"));
        Assert.Equal("message", message.StringHeader(":event-type"));
        Assert.Equal("application/cbor", message.StringHeader(":content-type"));

        var value = CborCodec
            .FromSchema(ChatEventSchema("UploadEvent"))
            .Deserialize(message.Payload.ToArray());
        var chat = Assert.IsType<ChatEvent.Message>(value);
        Assert.Equal(new Echo("one"), chat.Value);
    }

    [Fact]
    public async Task BidirectionalStreamingUsesEventStreamForRequestAndResponse()
    {
        var protocol = BuildServiceProtocol()
            .ForDuplexEventStreamOperation(
                InputOperation("Chat"),
                ChatEventSchema("ChatInputEvent"),
                ChatEventSchema("ChatOutputEvent")
            );

        var request = protocol.SerializeRequest(ToAsync([new ChatEvent.Message(new Echo("in"))]));

        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        Assert.Single(await ReadMessagesAsync(await BodyBytesAsync(request.Body)));

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(ToAsync([new ChatEvent.Message(new Echo("out"))]))
        );

        var events = await CollectAsync(protocol.DeserializeResponseEventsAsync(response));
        var message = Assert.IsType<ChatEvent.Message>(Assert.Single(events));
        Assert.Equal(new Echo("out"), message.Value);
    }

    [Fact]
    public void StreamingOperationsRejectUnsupportedInitialMembers()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            BuildServiceProtocol()
                .ForOutputEventStreamOperation(
                    OutputOperationWithInitial("WatchWithInitial"),
                    ChatEventSchema("WatchEvent")
                )
        );

        Assert.Contains("initial response members", ex.Message);
    }

    private static async Task<SmithyHttpClientResponse> ToClientResponseAsync(
        SmithyHttpServerResponse response
    )
    {
        var body = new MemoryStream();
        await foreach (var chunk in response.Body)
        {
            body.Write(chunk.Span);
        }

        var headers = response.Headers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );

        return new SmithyHttpClientResponse(
            (System.Net.HttpStatusCode)response.StatusCode,
            null,
            new SmithyHttpBody.Streaming(new MemoryStream(body.ToArray())),
            headers,
            headers,
            _ => null
        );
    }

    private static async Task<byte[]> BodyBytesAsync(SmithyHttpBody body)
    {
        var stream = new MemoryStream();
        await foreach (var chunk in BodyChunks(body))
        {
            stream.Write(chunk.Span);
        }

        return stream.ToArray();
    }

    private static IAsyncEnumerable<ReadOnlyMemory<byte>> BodyChunks(SmithyHttpBody body) =>
        body switch
        {
            SmithyHttpBody.EventStreaming eventStreaming => eventStreaming.Content,
            SmithyHttpBody.Bytes bytes => ToAsyncBytes([bytes.Content]),
            _ => ToAsyncBytes([]),
        };

    private static async Task<List<EventStreamMessage>> ReadMessagesAsync(byte[] framed)
    {
        var messages = new List<EventStreamMessage>();
        await foreach (
            var message in EventStreamMessageReader.ReadAllAsync(new MemoryStream(framed))
        )
        {
            messages.Add(message);
        }

        return messages;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async IAsyncEnumerable<ReadOnlyMemory<byte>> ToAsyncBytes(
        IEnumerable<byte[]> values
    )
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }
}

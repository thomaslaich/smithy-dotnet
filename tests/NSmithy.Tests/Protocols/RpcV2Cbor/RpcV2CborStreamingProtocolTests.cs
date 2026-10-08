using NSmithy.Codecs.Cbor;
using NSmithy.EventStream;
using NSmithy.Http;
using NSmithy.Protocols.RpcV2Cbor;
using Nsmithy.Tests.Rpcv2cbor;

namespace NSmithy.Tests.Protocols.RpcV2Cbor;

public sealed class RpcV2CborStreamingProtocolTests
{
    private static IServiceProtocol BuildServiceProtocol() =>
        new RpcV2CborProtocol().ForService(FixturesSchema.Schema);

    private static async IAsyncEnumerable<ChatEvent> NoEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// An event stream changes how the body is framed, not what the initial request has to satisfy,
    /// so the surrounding members are validated exactly as on a unary operation. The stream member
    /// itself is skipped — hence a single error here, not one complaining about <c>events</c>.
    /// </summary>
    [Fact]
    public void InputEventStreamValidatesTheInitialRequest()
    {
        var protocol = BuildServiceProtocol().ForServerOperation(TalkSchema.Schema);

        Assert.NotNull(protocol.InputValidator);
        var error = Assert.Single(
            protocol.InputValidator.GetErrors(new TalkInput(Events: NoEvents(), Name: "x"))
        );
        Assert.Equal("/name", error.Path);
    }

    [Fact]
    public void DuplexEventStreamValidatesTheInitialRequest()
    {
        var protocol = BuildServiceProtocol().ForServerOperation(ConverseSchema.Schema);

        Assert.NotNull(protocol.InputValidator);
        var error = Assert.Single(
            protocol.InputValidator.GetErrors(new ConverseInput(Events: NoEvents(), Name: "x"))
        );
        Assert.Equal("/name", error.Path);
    }

    [Fact]
    public async Task ServerStreamingSerializesUnaryRequestAndReadsCborEventStream()
    {
        var protocol = BuildServiceProtocol()
            .ForOutputEventStreamOperation(WatchSchema.Schema, ChatEventSchema.Schema);

        var request = protocol.SerializeRequest(new WatchInput("start"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/service/Fixtures/operation/Watch", request.RequestUri);
        Assert.Equal("application/cbor", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        // The response is a live event stream, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new WatchOutput(ToAsync([new ChatEvent.Message(new Echo("one"))]))
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        var events = await CollectAsync(output.Events!);
        var message = Assert.IsType<ChatEvent.Message>(Assert.Single(events));
        Assert.Equal(new Echo("one"), message.Value);
    }

    [Fact]
    public async Task ClientStreamingSerializesEventStreamRequest()
    {
        var protocol = BuildServiceProtocol()
            .ForInputEventStreamOperation(UploadSchema.Schema, ChatEventSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadInput(ToAsync([new ChatEvent.Message(new Echo("one"))]))
        );

        Assert.Equal("/service/Fixtures/operation/Upload", request.RequestUri);
        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/cbor"], request.Headers["Accept"]);
        // Client streaming has a unary response, so it stays in Buffer mode.
        Assert.False(request.ExpectStreamingResponse);

        var framed = await BodyBytesAsync(request.Body);
        var message = Assert.Single(await ReadMessagesAsync(framed));
        Assert.Equal("event", message.StringHeader(":message-type"));
        Assert.Equal("message", message.StringHeader(":event-type"));
        Assert.Equal("application/cbor", message.StringHeader(":content-type"));

        var value = CborCodecFactory
            .Default.FromSchema(ChatEventSchema.Schema)
            .Deserialize(message.Payload.ToArray());
        var chat = Assert.IsType<ChatEvent.Message>(value);
        Assert.Equal(new Echo("one"), chat.Value);
    }

    [Fact]
    public async Task BidirectionalStreamingUsesEventStreamForRequestAndResponse()
    {
        var protocol = BuildServiceProtocol()
            .ForDuplexEventStreamOperation(
                ChatSchema.Schema,
                ChatEventSchema.Schema,
                ChatEventSchema.Schema
            );

        var request = protocol.SerializeRequest(
            new ChatInput(ToAsync([new ChatEvent.Message(new Echo("in"))]))
        );

        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        // Duplex streams the response too, so the runtime must read it in Stream mode.
        Assert.True(request.ExpectStreamingResponse);
        Assert.Single(await ReadMessagesAsync(await BodyBytesAsync(request.Body)));

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new ChatOutput(ToAsync([new ChatEvent.Message(new Echo("out"))]))
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        var events = await CollectAsync(output.Events!);
        var message = Assert.IsType<ChatEvent.Message>(Assert.Single(events));
        Assert.Equal(new Echo("out"), message.Value);
    }

    [Fact]
    public async Task ServerStreamingRoundTripsInitialResponseMembers()
    {
        var protocol = BuildServiceProtocol()
            .ForOutputEventStreamOperation(WatchWithInitialSchema.Schema, ChatEventSchema.Schema);

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new WatchWithInitialOutput(
                    Events: ToAsync([new ChatEvent.Message(new Echo("one"))]),
                    Name: "ready"
                )
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        Assert.Equal("ready", output.Name);
        var message = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(output.Events!))
        );
        Assert.Equal(new Echo("one"), message.Value);
    }

    // ---------------- characterization ----------------
    //
    // The server half of an input or duplex stream had no coverage: every streaming test above
    // drives the client. These pin what the wire actually carries and what the server reads back
    // out of it, so a protocol restructure has something to be measured against.

    [Fact]
    public async Task ClientStreamingRoundTripsInitialRequestMembersAndEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadWithInitialSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadWithInitialInput(
                Events: ToAsync([new ChatEvent.Message(new Echo("one"))]),
                Name: "ready"
            )
        );

        var input = await protocol.DeserializeRequestAsync(await ToServerRequestAsync(request));

        Assert.Equal("ready", input.Name);
        var message = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(input.Events!))
        );
        Assert.Equal(new Echo("one"), message.Value);
    }

    /// <summary>
    /// The initial request is its own framed message, ahead of the events, and is typed
    /// <c>initial-request</c> — a peer distinguishes it from an event by that header alone.
    /// </summary>
    [Fact]
    public async Task ClientStreamingEmitsTheInitialRequestBeforeTheEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadWithInitialSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadWithInitialInput(
                Events: ToAsync([
                    new ChatEvent.Message(new Echo("one")),
                    new ChatEvent.Message(new Echo("two")),
                ]),
                Name: "ready"
            )
        );

        var messages = await ReadMessagesAsync(await BodyBytesAsync(request.Body));

        Assert.Equal(
            ["initial-request", "message", "message"],
            messages.Select(m => m.StringHeader(":event-type"))
        );
    }

    /// <summary>
    /// The mirror of the test above: a shape whose only member is the stream has no initial
    /// request to send, so nothing precedes the events.
    /// </summary>
    [Fact]
    public async Task ClientStreamingWithoutInitialMembersEmitsOnlyEvents()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadInput(ToAsync([new ChatEvent.Message(new Echo("one"))]))
        );

        var messages = await ReadMessagesAsync(await BodyBytesAsync(request.Body));

        Assert.Equal(["message"], messages.Select(m => m.StringHeader(":event-type")));
    }

    [Fact]
    public async Task ClientStreamingPreservesEventOrder()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadInput(
                ToAsync([
                    new ChatEvent.Message(new Echo("one")),
                    new ChatEvent.Message(new Echo("two")),
                    new ChatEvent.Message(new Echo("three")),
                ])
            )
        );

        var input = await protocol.DeserializeRequestAsync(await ToServerRequestAsync(request));

        Assert.Equal(
            ["one", "two", "three"],
            (await CollectAsync(input.Events!)).Select(e => ((ChatEvent.Message)e).Value.Message)
        );
    }

    [Fact]
    public async Task ClientStreamingRoundTripsAnEmptyEventStream()
    {
        var protocol = BuildServiceProtocol().ForOperation(UploadSchema.Schema);

        var request = protocol.SerializeRequest(new UploadInput(ToAsync(Array.Empty<ChatEvent>())));

        var input = await protocol.DeserializeRequestAsync(await ToServerRequestAsync(request));

        Assert.Empty(await CollectAsync(input.Events!));
    }

    [Fact]
    public async Task BidirectionalStreamingRoundTripsTheRequestToTheServer()
    {
        var protocol = BuildServiceProtocol().ForOperation(ChatSchema.Schema);

        var request = protocol.SerializeRequest(
            new ChatInput(ToAsync([new ChatEvent.Message(new Echo("in"))]))
        );

        var input = await protocol.DeserializeRequestAsync(await ToServerRequestAsync(request));

        var message = Assert.IsType<ChatEvent.Message>(
            Assert.Single(await CollectAsync(input.Events!))
        );
        Assert.Equal(new Echo("in"), message.Value);
    }

    /// <summary>
    /// A host hands the server a buffered or live body, never the client's outgoing event-stream
    /// body, so the framed bytes are replayed as a stream the way Kestrel would deliver them.
    /// </summary>
    private static async Task<SmithyHttpRequest> ToServerRequestAsync(SmithyHttpRequest request)
    {
        var framed = await BodyBytesAsync(request.Body);
        return new SmithyHttpRequest(request.Method, request.RequestUri)
        {
            Body = new SmithyHttpBody.Streaming(new MemoryStream(framed)),
            ContentType = request.ContentType,
        };
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

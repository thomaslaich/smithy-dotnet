using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.EventStream;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;
using Nsmithy.Tests.Rest;

namespace NSmithy.Tests.Protocols.RestJson;

public sealed class RestJson1ProtocolTests
{
    private static readonly IServiceProtocol RestService = new RestJson1Protocol().ForService(
        FixturesSchema.Schema
    );

    private static IOperationProtocol<TIn, TOut> Protocol<TIn, TOut>(
        OperationSchema<TIn, TOut> operation
    ) => RestService.ForOperation(operation);

    // A required member bound to a header or query string never reaches the body codec, and a
    // required value-type member has already defaulted by the time the validator runs, so this is
    // the only layer that can tell the caller they left one out.
    [Fact]
    public void RestJson1ProtocolRejectsAMissingRequiredQueryMember()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count");
        request.Headers["X-Tenant"] = ["acme"];

        var exception = Assert.Throws<MissingRequiredMemberException>(() =>
            Protocol(CountSchema.Schema).DeserializeRequest(request)
        );

        Assert.Equal("limit", exception.MemberName);
    }

    [Fact]
    public void RestJson1ProtocolRejectsAMissingRequiredHeaderMember()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count?limit=10");

        var exception = Assert.Throws<MissingRequiredMemberException>(() =>
            Protocol(CountSchema.Schema).DeserializeRequest(request)
        );

        Assert.Equal("tenant", exception.MemberName);
    }

    [Fact]
    public void RestJson1ProtocolAcceptsRequiredBoundMembersWhenPresent()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/count?limit=10");
        request.Headers["X-Tenant"] = ["acme"];

        var input = Protocol(CountSchema.Schema).DeserializeRequest(request);

        Assert.Equal(new CountInput(Limit: 10, Tenant: "acme"), input);
    }

    [Fact]
    public void RestJson1ProtocolExcludesHostAndContentLengthFromEmptyPrefixHeaders()
    {
        var request = new SmithyHttpRequest(HttpMethod.Get, "/headers");
        request.Headers["X-Extra-Trace"] = ["abc"];
        request.Headers["Host"] = ["example.com"];
        request.Headers["Content-Length"] = ["0"];

        var input = Protocol(EmptyPrefixHeadersSchema.Schema).DeserializeRequest(request);

        Assert.Equal("abc", input.ExtraHeaders.Values["X-Extra-Trace"]);
        Assert.DoesNotContain("Host", input.ExtraHeaders.Values.Keys);
        Assert.DoesNotContain("Content-Length", input.ExtraHeaders.Values.Keys);
    }

    [Fact]
    public void RestJson1ProtocolSerializesMediaTypeAndFractionalEpochSecondsQueryValues()
    {
        var input = new QueryValuesInput(
            Created: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(250),
            Media: "hello world"
        );

        var request = Protocol(QueryValuesSchema.Schema).SerializeRequest(input);

        Assert.Equal("/values?media=aGVsbG8gd29ybGQ%3D&created=1577836800.25", request.RequestUri);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesMediaTypeAndFractionalEpochSecondsQueryValues()
    {
        var request = new SmithyHttpRequest(
            HttpMethod.Get,
            "/values?media=aGVsbG8gd29ybGQ%3D&created=1577836800.25"
        );

        var input = Protocol(QueryValuesSchema.Schema).DeserializeRequest(request);

        Assert.Equal("hello world", input.Media);
        Assert.Equal(
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(250),
            input.Created
        );
    }

    [Fact]
    public void RestJson1ProtocolRoundTripsStringEnumHeaderListWithQuotedComma()
    {
        var operation = EnumHeaderListSchema.Schema;
        var input = new EnumHeaderListInput(
            new HeaderStatusList([HeaderStatus.ACTIVEBLUE, HeaderStatus.PENDING])
        );

        var request = Protocol(operation).SerializeRequest(input);
        var decoded = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("\"ACTIVE,BLUE\", PENDING", request.Headers["X-Status"].Single());
        Assert.Equal(input.Statuses.Values, decoded.Statuses.Values);
    }

    [Fact]
    public void RestJson1ProtocolSerializesStreamingBlobPayloadWithoutBuffering()
    {
        var operation = UploadUserAvatarSchema.Schema;
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        payload.Position = 2;
        var input = new UploadUserAvatarInput(Checksum: "abc123", Payload: payload, UserId: "ada");

        var request = Protocol(operation).SerializeRequest(input);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/users/ada/avatar", request.RequestUri);
        Assert.Equal("abc123", request.Headers["X-Checksum"].Single());
        Assert.Equal("application/octet-stream", request.ContentType);
        var body = Assert.IsType<SmithyHttpBody.Streaming>(request.Body);
        Assert.Same(payload, body.Content);
        Assert.Equal(payload.Length - 2, body.ContentLength);
        Assert.False(request.ExpectStreamingResponse);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesStreamingBlobPayloadWithoutBuffering()
    {
        var operation = UploadUserAvatarSchema.Schema;
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var request = new SmithyHttpRequest(HttpMethod.Put, "/users/ada/avatar")
        {
            Body = new SmithyHttpBody.Streaming(payload, payload.Length),
            ContentType = "application/octet-stream",
        };
        request.Headers["X-Checksum"] = ["abc123"];

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("ada", input.UserId);
        Assert.Equal("abc123", input.Checksum);
        Assert.Same(payload, input.Payload);
    }

    [Fact]
    public void RestJson1ProtocolDeserializesStreamingBlobResponseWithoutBuffering()
    {
        var operation = GetUserAvatarSchema.Schema;
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var response = new SmithyHttpClientResponse(
            HttpStatusCode.OK,
            null,
            new SmithyHttpBody.Streaming(payload),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["ETag"] = ["etag-1"],
            },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = ["application/octet-stream"],
            }
        );

        var output = Protocol(operation).DeserializeResponse(response);

        Assert.Equal("etag-1", output.ETag);
        Assert.Same(payload, output.Payload);
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesStreamingBlobResponseWithoutBuffering()
    {
        var operation = GetUserAvatarSchema.Schema;
        var payload = new MemoryStream("avatar bytes"u8.ToArray());
        var output = new GetUserAvatarOutput("etag-1", payload);

        var response = Protocol(operation).SerializeResponse(output);

        Assert.Equal((int)HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("etag-1", response.Headers["ETag"].Single());
        Assert.Equal("application/octet-stream", response.Headers["Content-Type"].Single());
        Assert.Equal("avatar bytes", Encoding.UTF8.GetString(await DrainAsync(response)));

        // The handler handed the stream over; the runtime disposes it once written.
        Assert.False(payload.CanRead);
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesAndReadsOutputEventStream()
    {
        var protocol = Protocol(WatchSchema.Schema);

        var request = protocol.SerializeRequest(new WatchInput("ada"));

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/streams/ada", request.RequestUri);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        Assert.True(request.ExpectStreamingResponse);

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new WatchOutput(
                    Events: ToAsync<ChatEvents>([new ChatEvents.Message(new Echo("one"))]),
                    StreamId: "s-1"
                )
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        Assert.Equal("s-1", output.StreamId);
        var message = Assert.IsType<ChatEvents.Message>(
            Assert.Single(await CollectAsync(output.Events))
        );
        Assert.Equal(new Echo("one"), message.Value);
    }

    [Fact]
    public async Task RestJson1ProtocolSerializesInputEventStreamRequest()
    {
        var protocol = Protocol(UploadSchema.Schema);

        var request = protocol.SerializeRequest(
            new UploadInput(
                Events: ToAsync<ChatEvents>([new ChatEvents.Message(new Echo("one"))]),
                StreamId: "s-1"
            )
        );

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/streams/s-1", request.RequestUri);
        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/json"], request.Headers["Accept"]);
        Assert.False(request.ExpectStreamingResponse);

        var messages = await ReadMessagesAsync(await BodyBytesAsync(request.Body));
        var message = Assert.Single(messages);
        Assert.Equal("event", message.StringHeader(":message-type"));
        Assert.Equal("message", message.StringHeader(":event-type"));
        Assert.Equal("application/json", message.StringHeader(":content-type"));

        var input = protocol.DeserializeRequest(
            new SmithyHttpRequest(HttpMethod.Post, "/streams/s-1")
            {
                Body = new SmithyHttpBody.Streaming(
                    new MemoryStream(await BodyBytesAsync(request.Body))
                ),
                ContentType = "application/vnd.amazon.eventstream",
            }
        );
        var chat = Assert.IsType<ChatEvents.Message>(
            Assert.Single(await CollectAsync(input.Events))
        );
        Assert.Equal(new Echo("one"), chat.Value);
    }

    [Fact]
    public async Task RestJson1ProtocolUsesEventStreamForDuplexRequestAndResponse()
    {
        var protocol = Protocol(ChatSchema.Schema);

        var request = protocol.SerializeRequest(
            new ChatInput(
                Events: ToAsync<ChatEvents>([new ChatEvents.Message(new Echo("in"))]),
                StreamId: "s-1"
            )
        );

        Assert.Equal("application/vnd.amazon.eventstream", request.ContentType);
        Assert.Equal(["application/vnd.amazon.eventstream"], request.Headers["Accept"]);
        Assert.True(request.ExpectStreamingResponse);
        Assert.Single(await ReadMessagesAsync(await BodyBytesAsync(request.Body)));

        var response = await ToClientResponseAsync(
            protocol.SerializeResponse(
                new ChatOutput(
                    Events: ToAsync<ChatEvents>([new ChatEvents.Message(new Echo("out"))]),
                    StreamId: "s-2"
                )
            )
        );

        var output = await protocol.DeserializeResponseAsync(response);
        Assert.Equal("s-2", output.StreamId);
        var message = Assert.IsType<ChatEvents.Message>(
            Assert.Single(await CollectAsync(output.Events))
        );
        Assert.Equal(new Echo("out"), message.Value);
    }

    private static async Task<byte[]> DrainAsync(SmithyHttpServerResponse response)
    {
        var buffer = new MemoryStream();
        await foreach (var chunk in response.Body)
        {
            buffer.Write(chunk.Span);
        }

        return buffer.ToArray();
    }

    private static async Task<SmithyHttpClientResponse> ToClientResponseAsync(
        SmithyHttpServerResponse response
    )
    {
        var headers = response.Headers.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase
        );
        return new SmithyHttpClientResponse(
            (HttpStatusCode)response.StatusCode,
            null,
            new SmithyHttpBody.Streaming(new MemoryStream(await DrainAsync(response))),
            headers,
            headers
        );
    }

    private static async Task<byte[]> BodyBytesAsync(SmithyHttpBody body)
    {
        var buffer = new MemoryStream();
        await foreach (var chunk in BodyChunks(body))
        {
            buffer.Write(chunk.Span);
        }

        return buffer.ToArray();
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

    [Fact]
    public void QueryParameterRoundTripPreservesNonNullToken()
    {
        var operation = ListItemsSchema.Schema;

        // Client serializes request with non-null nextToken
        var request = Protocol(operation)
            .SerializeRequest(new ListItemsInput(NextToken: "page2-token", PageSize: 10));

        Assert.Contains("nextToken=page2-token", request.RequestUri);
        Assert.Contains("pageSize=10", request.RequestUri);

        // Server deserializes the same request
        var deserializedInput = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("page2-token", deserializedInput.NextToken);
        Assert.Equal(10, deserializedInput.PageSize);
    }

    [Fact]
    public void QueryParameterRoundTripHandlesNullToken()
    {
        var operation = ListItemsSchema.Schema;

        // First page: no nextToken
        var request = Protocol(operation).SerializeRequest(new ListItemsInput(NextToken: null));

        Assert.DoesNotContain("nextToken", request.RequestUri);

        var input = Protocol(operation).DeserializeRequest(request);
        Assert.Null(input.NextToken);
    }

    [Fact]
    public void DeserializeReadsQueryFromServerStyleRequestUri()
    {
        var operation = ListCitiesSchema.Schema;

        // Exactly what SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync builds:
        // a relative path + query string, query params in the order the client sent them.
        var request = new SmithyHttpRequest(HttpMethod.Get, "/cities?pageSize=3&nextToken=LAX");

        var input = Protocol(operation).DeserializeRequest(request);

        Assert.Equal("LAX", input.NextToken);
        Assert.Equal(3, input.PageSize);
    }

    [Fact]
    public async Task CreateSmithyHttpRequestPreservesQueryString()
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/cities";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(
            "?pageSize=3&nextToken=LAX"
        );

        var request = await NSmithy.Server.AspNetCore.SmithyAspNetCoreHost.ToSmithyRequestAsync(
            httpContext
        );

        Assert.Equal("/cities?pageSize=3&nextToken=LAX", request.RequestUri);
    }
}

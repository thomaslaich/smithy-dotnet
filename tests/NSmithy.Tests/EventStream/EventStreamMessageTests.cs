using System.Text;
using NSmithy.EventStream;

namespace NSmithy.Tests.EventStream;

public sealed class EventStreamMessageTests
{
    // The canonical aws-c-event-stream "empty message" test vector.
    private const string EmptyMessageHex = "000000100000000005c248eb7d98c8ff";

    // One :event-type string header + a JSON payload, cross-checked against zlib crc32.
    private const string HeaderAndPayloadHex =
        "0000003100000014e3b99a220b3a6576656e742d747970650700056368756e6b"
        + "7b22666f6f223a22626172227dde845d7f";

    [Fact]
    public void EncodesTheEmptyMessageToTheCanonicalVector()
    {
        var message = new EventStreamMessage(
            new Dictionary<string, EventStreamHeaderValue>(),
            ReadOnlyMemory<byte>.Empty
        );

        Assert.Equal(EmptyMessageHex, Convert.ToHexStringLower(message.Encode()));
    }

    [Fact]
    public void EncodesHeaderAndPayloadToTheGoldenVector()
    {
        var message = new EventStreamMessage(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text("chunk"),
            },
            Encoding.UTF8.GetBytes("""{"foo":"bar"}""")
        );

        Assert.Equal(HeaderAndPayloadHex, Convert.ToHexStringLower(message.Encode()));
    }

    [Fact]
    public void DecodesTheGoldenVector()
    {
        var message = EventStreamMessage.Decode(Convert.FromHexString(HeaderAndPayloadHex));

        Assert.Equal("chunk", message.StringHeader(EventStreamHeaders.EventType));
        Assert.Equal("""{"foo":"bar"}""", Encoding.UTF8.GetString(message.Payload.Span));
    }

    [Fact]
    public void RoundTripsEveryHeaderValueType()
    {
        var headers = new Dictionary<string, EventStreamHeaderValue>
        {
            ["bool-true"] = new EventStreamHeaderValue.Bool(true),
            ["bool-false"] = new EventStreamHeaderValue.Bool(false),
            ["byte"] = new EventStreamHeaderValue.Signed8(-7),
            ["short"] = new EventStreamHeaderValue.Signed16(-12345),
            ["int"] = new EventStreamHeaderValue.Signed32(int.MinValue),
            ["long"] = new EventStreamHeaderValue.Signed64(long.MaxValue),
            ["bytes"] = new EventStreamHeaderValue.Blob([1, 2, 3, 255]),
            ["string"] = new EventStreamHeaderValue.Text("héllo ✓"),
            ["timestamp"] = new EventStreamHeaderValue.Timestamp(
                DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123)
            ),
            ["uuid"] = new EventStreamHeaderValue.Uuid(
                Guid.Parse("6f1e6b9a-98e2-4e21-8f6e-2f0a2f8f4e1c")
            ),
        };
        var payload = "payload"u8.ToArray();

        var decoded = EventStreamMessage.Decode(new EventStreamMessage(headers, payload).Encode());

        Assert.Equal(payload, decoded.Payload.ToArray());
        Assert.Equal(headers.Count, decoded.Headers.Count);
        foreach (var (name, value) in headers)
        {
            Assert.Equal(value, decoded.Headers[name]);
        }
    }

    [Fact]
    public void RejectsCorruptedPreludeCrc()
    {
        var bytes = Convert.FromHexString(HeaderAndPayloadHex);
        bytes[9] ^= 0xFF; // flip a prelude-CRC byte

        var ex = Assert.Throws<InvalidDataException>(() => EventStreamMessage.Decode(bytes));
        Assert.Contains("prelude", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsCorruptedPayload()
    {
        var bytes = Convert.FromHexString(HeaderAndPayloadHex);
        bytes[^6] ^= 0xFF; // flip a payload byte; the message CRC no longer matches

        var ex = Assert.Throws<InvalidDataException>(() => EventStreamMessage.Decode(bytes));
        Assert.Contains("CRC", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTruncatedMessage()
    {
        var bytes = Convert.FromHexString(HeaderAndPayloadHex);

        Assert.Throws<InvalidDataException>(() =>
            EventStreamMessage.Decode(bytes.AsMemory(0, bytes.Length - 3))
        );
    }

    [Fact]
    public async Task ReaderYieldsConsecutiveMessagesAndStopsAtEof()
    {
        var one = new EventStreamMessage(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text("one"),
            },
            "first"u8.ToArray()
        ).Encode();
        var two = new EventStreamMessage(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text("two"),
            },
            "second"u8.ToArray()
        ).Encode();
        using var stream = new MemoryStream([.. one, .. two]);

        var messages = new List<EventStreamMessage>();
        await foreach (var message in EventStreamMessageReader.ReadAllAsync(stream))
        {
            messages.Add(message);
        }

        Assert.Equal(2, messages.Count);
        Assert.Equal("one", messages[0].StringHeader(EventStreamHeaders.EventType));
        Assert.Equal("second", Encoding.UTF8.GetString(messages[1].Payload.Span));
    }

    [Fact]
    public async Task ReaderThrowsOnMidMessageTruncation()
    {
        var bytes = new EventStreamMessage(
            new Dictionary<string, EventStreamHeaderValue>(),
            "payload"u8.ToArray()
        ).Encode();
        using var stream = new MemoryStream(bytes[..^2]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in EventStreamMessageReader.ReadAllAsync(stream)) { }
        });
    }

    [Fact]
    public async Task ReaderCompletesImmediatelyOnEmptyStream()
    {
        using var stream = new MemoryStream();

        await foreach (var _ in EventStreamMessageReader.ReadAllAsync(stream))
        {
            Assert.Fail("Empty stream must yield no messages.");
        }
    }
}

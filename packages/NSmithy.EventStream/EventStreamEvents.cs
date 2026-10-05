using System.Runtime.CompilerServices;

namespace NSmithy.EventStream;

/// <summary>
/// Smithy event messages over <see cref="EventStreamMessage"/> framing, shared by every protocol
/// that streams events as <c>application/vnd.amazon.eventstream</c>: an event carries its union
/// member name in <c>:event-type</c> and its payload's media type in <c>:content-type</c>, and an
/// <c>error</c> or <c>exception</c> message ends the stream. The payload itself stays opaque; each
/// protocol encodes it with its own body codec.
/// </summary>
public static class EventStreamEvents
{
    public static EventStreamMessage Create(
        string eventType,
        ReadOnlyMemory<byte> payload,
        string contentType
    ) =>
        new(
            new Dictionary<string, EventStreamHeaderValue>
            {
                [EventStreamHeaders.MessageType] = new EventStreamHeaderValue.Text(
                    EventStreamHeaders.EventMessageType
                ),
                [EventStreamHeaders.EventType] = new EventStreamHeaderValue.Text(eventType),
                [EventStreamHeaders.ContentType] = new EventStreamHeaderValue.Text(contentType),
            },
            payload
        );

    /// <summary>Encodes each value as one framed event message.</summary>
    public static async IAsyncEnumerable<ReadOnlyMemory<byte>> EncodeAsync<T>(
        IAsyncEnumerable<T> events,
        Func<T, string> eventTypeOf,
        Func<T, byte[]> serialize,
        string contentType,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(eventTypeOf);
        ArgumentNullException.ThrowIfNull(serialize);

        await foreach (
            var value in events.WithCancellation(cancellationToken).ConfigureAwait(false)
        )
        {
            yield return Create(eventTypeOf(value), serialize(value), contentType).Encode();
        }
    }

    /// <summary>
    /// Throws unless <paramref name="message"/> is an event: an <c>error</c> or <c>exception</c>
    /// message surfaces as the failure it reports, and any other message type is malformed.
    /// </summary>
    public static void EnsureEvent(EventStreamMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var messageType = message.StringHeader(EventStreamHeaders.MessageType);
        switch (messageType)
        {
            case EventStreamHeaders.EventMessageType:
                return;
            case EventStreamHeaders.ErrorMessageType:
                var code = message.StringHeader(EventStreamHeaders.ErrorCode) ?? "UnknownError";
                var text = message.StringHeader(EventStreamHeaders.ErrorMessage);
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(text) ? code : $"{code}: {text}"
                );
            case EventStreamHeaders.ExceptionMessageType:
                var type =
                    message.StringHeader(EventStreamHeaders.ExceptionType) ?? "UnknownException";
                throw new InvalidOperationException($"Event stream exception: {type}.");
            default:
                throw new InvalidDataException(
                    $"Unknown event stream message type '{messageType ?? "<missing>"}'."
                );
        }
    }

    /// <summary>
    /// The payload of an event whose <c>:content-type</c>, when present, is
    /// <paramref name="contentType"/> (parameters such as <c>charset</c> are ignored).
    /// </summary>
    public static ReadOnlyMemory<byte> ReadPayload(EventStreamMessage message, string contentType)
    {
        EnsureEvent(message);
        var actual = message.StringHeader(EventStreamHeaders.ContentType);
        if (actual is not null && !IsMediaType(actual, contentType))
        {
            throw new InvalidDataException(
                $"Expected event payload content type '{contentType}' but received '{actual}'."
            );
        }

        return message.Payload;
    }

    private static bool IsMediaType(string value, string mediaType)
    {
        var separator = value.IndexOf(';', StringComparison.Ordinal);
        var bare = (separator >= 0 ? value[..separator] : value).Trim();
        return string.Equals(bare, mediaType, StringComparison.OrdinalIgnoreCase);
    }
}

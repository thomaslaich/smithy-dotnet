using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace NSmithy.EventStream;

/// <summary>
/// Incrementally reads framed <c>vnd.amazon.eventstream</c> messages from a stream. Ends cleanly
/// on EOF at a message boundary; throws <see cref="InvalidDataException"/> on mid-message
/// truncation or CRC failure.
/// </summary>
public static class EventStreamMessageReader
{
    private const int PreludeLength = 12;

    public static async IAsyncEnumerable<EventStreamMessage> ReadAllAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(stream);

        var prelude = new byte[PreludeLength];
        while (await TryReadExactAsync(stream, prelude, cancellationToken).ConfigureAwait(false))
        {
            var totalLength = EventStreamMessage.ReadLength(prelude.AsSpan(0, 4), "total length");
            if (totalLength < PreludeLength + 4)
            {
                throw new InvalidDataException(
                    $"Event stream message declares impossible length {totalLength}."
                );
            }

            var message = new byte[totalLength];
            prelude.CopyTo(message.AsSpan());
            if (
                !await TryReadExactAsync(stream, message.AsMemory(PreludeLength), cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                throw new InvalidDataException("Truncated event stream message.");
            }

            yield return EventStreamMessage.Decode(message);
        }
    }

    private static async ValueTask<bool> TryReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken
    )
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream
                .ReadAsync(buffer[read..], cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new InvalidDataException("Truncated event stream message prelude.");
            }

            read += count;
        }

        return true;
    }
}

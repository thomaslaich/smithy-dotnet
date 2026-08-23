using System.Buffers;
using System.Text.Json;

namespace NSmithy.Codecs.Json;

/// <summary>
/// An <see cref="IBufferWriter{T}"/> backed by <see cref="ArrayPool{T}"/>, used as
/// scratch space while serializing.
/// </summary>
/// <remarks>
/// Replaces a <see cref="MemoryStream"/> whose growth allocated a fresh array per
/// doubling and whose <c>ToArray</c> then copied the whole payload once more. Here
/// the intermediate buffers come from the pool and are returned, so a serialize
/// call allocates only the array it hands back.
/// </remarks>
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
    private byte[]? buffer;
    private int written;

    public PooledByteBufferWriter(int initialCapacity) =>
        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));

    public void Reset(int initialCapacity)
    {
        if (buffer is not null)
        {
            throw new InvalidOperationException("The buffer writer is already in use.");
        }

        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));
        written = 0;
    }

    public int WrittenCount => written;

    public ReadOnlySpan<byte> WrittenSpan => Current.AsSpan(0, written);

    private byte[] Current =>
        buffer ?? throw new ObjectDisposedException(nameof(PooledByteBufferWriter));

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Current.Length - written);
        written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return Current.AsMemory(written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return Current.AsSpan(written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        if (sizeHint < 1)
            sizeHint = 1;

        var current = Current;
        if (sizeHint <= current.Length - written)
            return;

        var next = ArrayPool<byte>.Shared.Rent(Math.Max(current.Length * 2, written + sizeHint));
        current.AsSpan(0, written).CopyTo(next);
        buffer = next;
        ArrayPool<byte>.Shared.Return(current);
    }

    public void Dispose()
    {
        var current = buffer;
        if (current is null)
            return;

        buffer = null;
        ArrayPool<byte>.Shared.Return(current);
    }
}

/// <summary>
/// Reuses the small <see cref="PooledByteBufferWriter"/> wrapper while continuing to return its
/// potentially large byte array to <see cref="ArrayPool{T}"/> after every serialization.
/// </summary>
internal static class PooledByteBufferWriterCache
{
    [ThreadStatic]
    private static PooledByteBufferWriter? cached;

    public static PooledByteBufferWriter Rent(int initialCapacity)
    {
        var writer = cached;
        cached = null;
        if (writer is null)
        {
            return new PooledByteBufferWriter(initialCapacity);
        }

        writer.Reset(initialCapacity);
        return writer;
    }

    public static void Return(PooledByteBufferWriter writer)
    {
        writer.Dispose();
        cached ??= writer;
    }
}

/// <summary>
/// A per-thread <see cref="Utf8JsonWriter"/> that is reset rather than
/// reallocated between serialize calls.
/// </summary>
/// <remarks>
/// A <see cref="Utf8JsonWriter"/> carries an internal buffer, so constructing one
/// per call was a per-call allocation on top of the payload itself. Reuse is safe
/// here because serialization is synchronous and never reentrant — the compiled
/// writers only write to the writer they are handed.
/// <para>
/// Writer options are left at their defaults deliberately: they determine escaping
/// behaviour, and the wire output must not change.
/// </para>
/// </remarks>
internal static class JsonWriterCache
{
    [ThreadStatic]
    private static Utf8JsonWriter? cached;

    public static Utf8JsonWriter Rent(IBufferWriter<byte> destination)
    {
        var writer = cached;
        cached = null;
        if (writer is null)
        {
            return new Utf8JsonWriter(destination);
        }

        writer.Reset(destination);
        return writer;
    }

    /// <summary>
    /// Detaches the writer from the pooled buffer, so a returned buffer is not
    /// still referenced by the cached writer.
    /// </summary>
    public static void Return(Utf8JsonWriter writer)
    {
        writer.Reset(Stream.Null);
        cached ??= writer;
    }
}

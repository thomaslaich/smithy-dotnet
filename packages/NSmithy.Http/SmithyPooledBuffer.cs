using System.Buffers;

namespace NSmithy.Http;

/// <summary>
/// A growable buffer rented from <see cref="ArrayPool{T}"/>, for a body that is written once and
/// then handed on, such as a serialized response. Disposing it returns the array to the pool, so
/// nothing may read <see cref="WrittenMemory"/> afterwards.
/// </summary>
public sealed class SmithyPooledBuffer : IBufferWriter<byte>, IDisposable
{
    private byte[]? buffer;
    private int written;

    public SmithyPooledBuffer(int initialCapacity = 256) =>
        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));

    public int WrittenCount => written;

    public ReadOnlyMemory<byte> WrittenMemory => Current.AsMemory(0, written);

    private byte[] Current =>
        buffer ?? throw new ObjectDisposedException(nameof(SmithyPooledBuffer));

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

    public void Dispose()
    {
        var current = buffer;
        if (current is null)
        {
            return;
        }

        buffer = null;
        ArrayPool<byte>.Shared.Return(current);
    }

    private void EnsureCapacity(int sizeHint)
    {
        var current = Current;
        if (Math.Max(sizeHint, 1) <= current.Length - written)
        {
            return;
        }

        var next = ArrayPool<byte>.Shared.Rent(
            Math.Max(current.Length * 2, written + Math.Max(sizeHint, 1))
        );
        current.AsSpan(0, written).CopyTo(next);
        buffer = next;
        ArrayPool<byte>.Shared.Return(current);
    }
}

using System.Formats.Cbor;

namespace NSmithy.Codecs.Cbor;

internal static class CborWriterCache
{
    private const int MaxRetainedBytes = 64 * 1024;

    [ThreadStatic]
    private static CborWriter? cached;

    public static CborWriter Rent()
    {
        var writer = cached;
        cached = null;
        if (writer is null)
        {
            return new CborWriter(CborConformanceMode.Lax);
        }

        return writer;
    }

    public static void Return(CborWriter writer)
    {
        var bytesWritten = writer.BytesWritten;
        writer.Reset();
        if (bytesWritten <= MaxRetainedBytes)
        {
            cached ??= writer;
        }
    }
}

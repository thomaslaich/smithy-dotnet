using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

internal sealed class CompiledCborCodec<T>(Schema<T> schema, bool materializeTopLevelDefaults)
    : ICodec<T>
{
    private readonly ICborValueWriter<T> valueWriter = CborWriterCompiler.Compile(
        schema,
        materializeTopLevelDefaults
    );
    private readonly ICborValueReader<T> valueReader = CborReaderCompiler.Compile(schema);

    public byte[] Serialize(T value)
    {
        var writer = CborWriterCache.Rent();
        try
        {
            valueWriter.Write(writer, value);
            return writer.Encode();
        }
        finally
        {
            CborWriterCache.Return(writer);
        }
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length == 0)
        {
            return default!;
        }

        var reader = new CborReader(payload, CborConformanceMode.Lax);
        return valueReader.Read(reader);
    }
}

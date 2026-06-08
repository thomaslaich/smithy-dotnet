using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

public static partial class JsonCodec
{
    private sealed class CompiledJsonCodec<T>(Schema<T> schema) : IJsonCodec<T>
    {
        private readonly IJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(schema);
        private readonly IJsonValueReader<T> valueReader = JsonReaderCompiler.Compile(schema);

        public byte[] Serialize(T value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                valueWriter.Write(writer, value);
            }

            return stream.ToArray();
        }

        public T Deserialize(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var document = JsonDocument.Parse(payload);
            return valueReader.Read(document.RootElement);
        }
    }

    private sealed class CompiledJsonProjectionCodec<T>(
        StructProjection<T> projection,
        bool materializeTopLevelDefaults
    ) : IProjectionCodec<T>
    {
        private readonly StructureJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
            projection,
            materializeTopLevelDefaults
        );

        public byte[] Serialize(T value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                valueWriter.Write(writer, value);
            }

            return stream.ToArray();
        }

        public void ReadInto(byte[] payload, object builder)
        {
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(builder);

            using var document = JsonDocument.Parse(payload);
            ReadProjectionInto(projection, document.RootElement, builder);
        }
    }
}

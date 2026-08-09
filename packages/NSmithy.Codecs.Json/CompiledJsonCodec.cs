using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal sealed class CompiledJsonCodec<T>(Schema<T> schema, bool materializeTopLevelDefaults)
    : IJsonCodec<T>
{
    private readonly IJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
        schema,
        materializeTopLevelDefaults
    );
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

internal sealed class CompiledJsonProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructureJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
        projection,
        materializeTopLevelDefaults
    );
    private readonly StructureJsonProjectionReader<TBuilder> valueReader =
        JsonReaderCompiler.Compile(projection);

    public byte[] Serialize(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            valueWriter.Write(writer, value);
        }

        return stream.ToArray();
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);

        using var document = JsonDocument.Parse(payload);
        valueReader.ReadInto(builder, document.RootElement);
    }
}

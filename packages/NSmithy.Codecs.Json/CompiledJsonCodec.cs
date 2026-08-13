using System.Text.Json;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal sealed class CompiledJsonCodec<T>(
    Schema<T> schema,
    bool materializeTopLevelDefaults,
    WireReadMode readMode
) : IJsonCodec<T>
{
    private readonly IJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
        schema,
        materializeTopLevelDefaults
    );
    private readonly IJsonValueReader<T> valueReader = JsonReaderCompiler.Compile(schema, readMode);

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
        using var document = JsonBody.Parse(payload);
        return valueReader.Read(document.RootElement);
    }
}

/// <summary>
/// The outermost step of reading a JSON payload: turning bytes into a document at all. A body that
/// is not JSON — unbalanced braces, a comment, a trailing comma, anything after the closing brace —
/// fails here, before a single member is read, and is the caller's mistake rather than a fault.
/// </summary>
internal static class JsonBody
{
    public static JsonDocument Parse(byte[] payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException exception)
        {
            throw MalformedRequestException.Serialization(
                $"Request body is not valid JSON: {exception.Message}"
            );
        }
    }
}

internal sealed class CompiledJsonProjectionCodec<T, TBuilder>(
    StructProjection<T, TBuilder> projection,
    bool materializeTopLevelDefaults,
    WireReadMode readMode
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructureJsonValueWriter<T> valueWriter = JsonWriterCompiler.Compile(
        projection,
        materializeTopLevelDefaults
    );
    private readonly StructureJsonProjectionReader<TBuilder> valueReader =
        JsonReaderCompiler.Compile(projection, readMode);

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

        using var document = JsonBody.Parse(payload);
        valueReader.ReadInto(builder, document.RootElement);
    }
}

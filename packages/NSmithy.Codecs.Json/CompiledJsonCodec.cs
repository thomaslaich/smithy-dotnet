using System.Text.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Json;

internal sealed class CompiledJsonCodec<T>(
    Schema<T> schema,
    bool materializeTopLevelDefaults,
    WireReadMode readMode,
    JsonPlans plans,
    IReadOnlyDictionary<ShapeId, Trait>? memberTraits = null,
    KeyValuePair<string, string>? discriminator = null
) : ICodec<T>
{
    private readonly JsonMemberPlan root = plans.ForRoot(schema, memberTraits, discriminator);

    // Size hint carried between calls: a codec instance serializes the same shape
    // repeatedly, so the previous payload size is a good guess at the next one and
    // usually avoids growing the scratch buffer at all.
    private int sizeHint = 256;

    public byte[] Serialize(T value)
    {
        var buffer = PooledByteBufferWriterCache.Rent(sizeHint);
        var writer = JsonWriterCache.Rent(buffer);
        try
        {
            var serializer = new JsonShapeSerializer(writer, root, materializeTopLevelDefaults);
            schema.Write(MemberIndex.Root, value, ref serializer);
            writer.Flush();
            sizeHint = buffer.WrittenCount;
            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            JsonWriterCache.Return(writer);
            PooledByteBufferWriterCache.Return(buffer);
        }
    }

    public T Deserialize(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var document = JsonBody.Parse(payload);
        var deserializer = new JsonShapeDeserializer(document.RootElement, root, readMode);
        return schema.Read(ref deserializer);
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
    WireReadMode readMode,
    JsonPlans plans
) : IProjectionCodec<T, TBuilder>
{
    private readonly StructSchema<T, TBuilder> source = projection.Source;
    private readonly JsonShapePlan plan = plans
        .ForTarget(projection.Source)!
        .Project(name => projection.GetMember(name) is not null);

    private int sizeHint = 256;

    public byte[] Serialize(T value)
    {
        var buffer = PooledByteBufferWriterCache.Rent(sizeHint);
        var writer = JsonWriterCache.Rent(buffer);
        try
        {
            JsonShapeSerializer.WriteObject(
                writer,
                plan,
                materializeTopLevelDefaults,
                source,
                value
            );
            writer.Flush();
            sizeHint = buffer.WrittenCount;
            return buffer.WrittenSpan.ToArray();
        }
        finally
        {
            JsonWriterCache.Return(writer);
            PooledByteBufferWriterCache.Return(buffer);
        }
    }

    public void ReadInto(byte[] payload, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(builder);

        using var document = JsonBody.Parse(payload);
        JsonShapeDeserializer.ReadMembers(
            document.RootElement,
            plan,
            source,
            builder,
            readMode,
            projection: true
        );
    }
}

using System.Buffers;

namespace NSmithy.Core.Serde;

/// <summary>
/// One-call convenience helpers built atop <see cref="ISmithyCodec.CreateSerializer"/> /
/// <see cref="ISmithyCodec.CreateDeserializer"/>. Provided as extension methods so the codec
/// interface stays minimal.
/// </summary>
public static class SmithyCodecExtensions
{
    /// <summary>Serialize <paramref name="shape"/> to a freshly allocated byte array.</summary>
    public static byte[] Serialize<T>(this ISmithyCodec codec, T shape)
        where T : ISerializableShape
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(shape);

        using var stream = new MemoryStream();
        using (var serializer = codec.CreateSerializer(stream))
        {
            shape.Serialize(serializer);
            serializer.Flush();
        }

        return stream.ToArray();
    }

    /// <summary>Deserialize a value of <typeparamref name="T"/> from <paramref name="source"/>.</summary>
    public static T Deserialize<T>(this ISmithyCodec codec, ReadOnlyMemory<byte> source)
        where T : IDeserializableShape<T>
    {
        ArgumentNullException.ThrowIfNull(codec);
        return codec.Deserialize<T>(new ReadOnlySequence<byte>(source));
    }

    /// <summary>Deserialize a value of <typeparamref name="T"/> from <paramref name="source"/>.</summary>
    public static T Deserialize<T>(this ISmithyCodec codec, ReadOnlySequence<byte> source)
        where T : IDeserializableShape<T>
    {
        ArgumentNullException.ThrowIfNull(codec);
        using var deserializer = codec.CreateDeserializer(source);
        return T.Deserialize(deserializer);
    }

    /// <summary>Serialize a value by directly driving the codec serializer.</summary>
    public static byte[] Serialize(this ISmithyCodec codec, Action<IShapeSerializer> write)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(write);

        using var stream = new MemoryStream();
        using (var serializer = codec.CreateSerializer(stream))
        {
            write(serializer);
            serializer.Flush();
        }

        return stream.ToArray();
    }

    /// <summary>Deserialize a value by directly driving the codec deserializer.</summary>
    public static T Deserialize<T>(
        this ISmithyCodec codec,
        ReadOnlyMemory<byte> source,
        Func<IShapeDeserializer, T> read
    )
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(read);
        return codec.Deserialize(new ReadOnlySequence<byte>(source), read);
    }

    /// <summary>Deserialize a value by directly driving the codec deserializer.</summary>
    public static T Deserialize<T>(
        this ISmithyCodec codec,
        ReadOnlySequence<byte> source,
        Func<IShapeDeserializer, T> read
    )
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(read);
        using var deserializer = codec.CreateDeserializer(source);
        return read(deserializer);
    }
}

using System.Formats.Cbor;
using System.Globalization;
using System.Numerics;
using NSmithy.Core;
using NSmithy.Core.Serde;
using static NSmithy.Codecs.Cbor.CborWire;

namespace NSmithy.Codecs.Cbor;

public interface ICborCodec<T> : ICodec<T> { }

public static class CborCodec
{
    /// <summary>
    /// Creates a codec for <paramref name="schema"/>. <paramref name="materializeTopLevelDefaults"/>
    /// controls whether the top-level structure writes members that carry a <c>@default</c> trait
    /// when they are null; nested structures always materialize their defaults. Client requests
    /// pass <c>false</c> (top-level defaults are skipped on the wire); server responses pass
    /// <c>true</c>.
    /// </summary>
    public static ICborCodec<T> FromSchema<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledCborCodec<T>(schema, materializeTopLevelDefaults);
    }

    public static IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledCborProjectionCodec<T, TBuilder>(
            projection,
            materializeTopLevelDefaults
        );
    }

    /// <summary>
    /// Serializes an error structure as a CBOR map, prefixing a <c>__type</c> discriminator
    /// entry that carries the absolute shape id. This is how rpcv2Cbor encodes error responses.
    /// </summary>
    public static byte[] SerializeError<T>(Schema<T> schema, T value, string typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(typeDiscriminator);
        if (schema.Resolved is not IStructSchema<T> structSchema)
        {
            throw new InvalidOperationException(
                "rpcv2Cbor errors must be backed by a structure schema."
            );
        }

        var visitor = new CborMemberWriterCompiler<T>(
            new CborWriterCompiler(materializeTopLevelDefaults: true),
            materializeDefaults: true
        );
        structSchema.VisitMembers(visitor);
        var writer = new CborWriter(CborConformanceMode.Lax);
        writer.WriteStartMap(null);
        writer.WriteTextString("__type");
        writer.WriteTextString(typeDiscriminator);
        foreach (var memberWriter in visitor.Writers)
        {
            memberWriter.Write(writer, value);
        }

        writer.WriteEndMap();
        return writer.Encode();
    }
}

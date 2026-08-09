using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

public interface IXmlCodec<T> : ICodec<T> { }

public static class XmlCodec
{
    public static IXmlCodec<T> FromSchema<T>(Schema<T> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledXmlCodec<T>(schema);
    }

    public static IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledXmlProjectionCodec<T, TBuilder>(projection);
    }
}

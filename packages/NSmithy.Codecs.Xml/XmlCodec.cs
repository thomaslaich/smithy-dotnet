using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

public interface IXmlCodec<T> : ICodec<T> { }

public static class XmlCodec
{
    public static IXmlCodec<T> FromSchema<T>(
        Schema<T> schema,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new CompiledXmlCodec<T>(schema, materializeTopLevelDefaults);
    }

    public static IProjectionCodec<T, TBuilder> FromProjection<T, TBuilder>(
        StructProjection<T, TBuilder> projection,
        bool materializeTopLevelDefaults = true
    )
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new CompiledXmlProjectionCodec<T, TBuilder>(projection, materializeTopLevelDefaults);
    }
}

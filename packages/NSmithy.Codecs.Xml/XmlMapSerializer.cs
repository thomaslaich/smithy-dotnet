using System.Xml.Linq;
using NSmithy.Core.Serde;

namespace NSmithy.Codecs.Xml;

internal sealed class XmlMapSerializer(XElement parent, string entryElementName = "entry")
    : IMapSerializer
{
    private readonly XElement parent = parent;
    private readonly string entryElementName = entryElementName;

    public void Entry<TState>(
        string key,
        TState state,
        Action<TState, IShapeSerializer> valueWriter
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(valueWriter);
        var entry = new XElement(entryElementName);
        entry.Add(new XElement("key", key));
        var valueElement = new XElement("value");
        entry.Add(valueElement);
        parent.Add(entry);
        valueWriter(state, new XmlShapeSerializer(valueElement));
    }
}

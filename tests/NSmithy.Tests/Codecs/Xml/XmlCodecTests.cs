using NSmithy.Codecs.Xml;
using NSmithy.Core.Serde;
using Nsmithy.Tests.Xml;

namespace NSmithy.Tests.Codecs.Xml;

public sealed class XmlCodecTests
{
    [Fact]
    public void DeserializesWrappedListUnderDefaultNamespace()
    {
        // Real AWS restXml responses (e.g. S3 ListBuckets) put a default xmlns on the
        // root that every descendant inherits, while the schema's element names are
        // unqualified. Element lookups must match on local name; namespace-sensitive
        // matching misses every child and the wrapped list comes back empty.
        var codec = XmlCodecFactory.Default.FromSchema(CatalogSchema.Schema);

        var xml =
            "<Catalog xmlns=\"urn:example\"><items><member>a</member><member>b</member></items></Catalog>";
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(["a", "b"], decoded.Items!.Values);
    }

    [Fact]
    public void SerializesDefaultNamespaceAcrossDescendants()
    {
        var xml = XmlCodecFactory
            .Default.FromSchema(NamespacedCatalogSchema.Schema)
            .SerializeText(new NamespacedCatalog(new ItemList(["a", "b"])));

        Assert.Equal(
            "<Catalog xmlns=\"urn:example\"><items><member>a</member><member>b</member></items></Catalog>",
            xml
        );
    }

    [Fact]
    public void DeserializesWhitespaceOnlyScalar()
    {
        var decoded = XmlCodecFactory
            .Default.FromSchema(Schemas.String)
            .DeserializeText("<String>  </String>");

        Assert.Equal("  ", decoded);
    }

    [Fact]
    public void DeserializesRealS3ListBucketsResponse()
    {
        // The exact shape of a real S3 ListBuckets response: a default xmlns on the root, a
        // non-flattened list whose items are named by the member's @xmlName ("Bucket") rather than
        // the default "member". Requires both the local-name element matching and the element-schema
        // trait overlay that carries the member's @xmlName.
        var codec = XmlCodecFactory.Default.FromSchema(ListAllMyBucketsResultSchema.Schema);

        var xml =
            "<ListAllMyBucketsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">"
            + "<Buckets><Bucket><Name>assets</Name></Bucket><Bucket><Name>logs</Name></Bucket></Buckets>"
            + "</ListAllMyBucketsResult>";
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(["assets", "logs"], decoded.Buckets!.Values.Select(bucket => bucket.Name));
    }

    [Fact]
    public void XmlCodecRoundTripsNestedStructure()
    {
        var input = new Person(Name: "Ada", Age: 36, Address: new Address("London"));
        var expectedXml =
            "<Person><name>Ada</name><age>36</age><address><city>London</city></address></Person>";
        var codec = XmlCodecFactory.Default.FromSchema(PersonSchema.Schema);

        var xml = codec.SerializeText(input);
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(expectedXml, xml);
        Assert.Equal(input, decoded);
    }
}

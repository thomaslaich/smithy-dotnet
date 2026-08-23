using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Codecs.Xml;

public sealed class XmlCodecTests
{
    public sealed record Address(string City);

    public sealed class AddressBuilder
    {
        public string? City { get; set; }
    }

    public sealed record Person(string Name, int Age, Address Address);

    public sealed class PersonBuilder
    {
        public string? Name { get; set; }

        public int Age { get; set; }

        public Address? Address { get; set; }
    }

    private sealed class AddressSerializer : IStructValueSerializer<Address>
    {
        public void WriteMembers<TWriter>(Address value, ref TWriter writer)
            where TWriter : struct, IStructMemberWriter => writer.WriteMember(0, value.City);
    }

    private sealed class PersonSerializer : IStructValueSerializer<Person>
    {
        public void WriteMembers<TWriter>(Person value, ref TWriter writer)
            where TWriter : struct, IStructMemberWriter
        {
            writer.WriteMember(0, value.Name);
            writer.WriteMember(1, value.Age);
            writer.WriteMember(2, value.Address);
        }
    }

    public sealed record Catalog(IReadOnlyList<string> Items);

    public sealed class CatalogBuilder
    {
        public IReadOnlyList<string>? Items { get; set; }
    }

    [Fact]
    public void DeserializesWrappedListUnderDefaultNamespace()
    {
        // Real AWS restXml responses (e.g. S3 ListBuckets) put a default xmlns on the
        // root that every descendant inherits, while the schema's element names are
        // unqualified. Element lookups must match on local name; namespace-sensitive
        // matching misses every child and the wrapped list comes back empty.
        var catalogSchema = Schemas
            .Structure<Catalog, CatalogBuilder>(new ShapeId("example", "Catalog"))
            .Optional(
                "items",
                static catalog => catalog.Items,
                static (builder, value) => builder.Items = value,
                Schemas.List<string>(new ShapeId("example", "ItemList"), Schemas.String)
            )
            .Build(
                static () => new CatalogBuilder(),
                static builder => new Catalog(builder.Items ?? [])
            );
        var codec = XmlCodec.FromSchema(catalogSchema);

        var xml =
            "<Catalog xmlns=\"urn:example\"><items><member>a</member><member>b</member></items></Catalog>";
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(["a", "b"], decoded.Items);
    }

    public sealed record S3Bucket(string Name);

    public sealed class S3BucketBuilder
    {
        public string? Name { get; set; }
    }

    public sealed record BucketList(IReadOnlyList<S3Bucket> Buckets);

    public sealed class BucketListBuilder
    {
        public IReadOnlyList<S3Bucket>? Buckets { get; set; }
    }

    private static Trait XmlName(string name) =>
        new(ShapeId.Parse("smithy.api#xmlName"), Document.From(name));

    [Fact]
    public void DeserializesRealS3ListBucketsResponse()
    {
        // The exact shape of a real S3 ListBuckets response: a default xmlns on the root, a
        // non-flattened list whose items are named by the member's @xmlName ("Bucket") rather than
        // the default "member". Requires both the local-name element matching and the element-schema
        // trait overlay that carries the member's @xmlName.
        var bucketSchema = Schemas
            .Structure<S3Bucket, S3BucketBuilder>(new ShapeId("example", "S3Bucket"))
            .Optional(
                "name",
                static bucket => bucket.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String,
                [XmlName("Name")]
            )
            .Build(
                static () => new S3BucketBuilder(),
                static builder => new S3Bucket(builder.Name!)
            );
        var listSchema = Schemas.List<S3Bucket>(
            new ShapeId("example", "S3BucketList"),
            bucketSchema,
            elementTraits: [XmlName("Bucket")]
        );
        var outputSchema = Schemas
            .Structure<BucketList, BucketListBuilder>(
                new ShapeId("example", "ListAllMyBucketsResult")
            )
            .Optional(
                "buckets",
                static output => output.Buckets,
                static (builder, value) => builder.Buckets = value,
                listSchema,
                [XmlName("Buckets")]
            )
            .Build(
                static () => new BucketListBuilder(),
                static builder => new BucketList(builder.Buckets ?? [])
            );
        var codec = XmlCodec.FromSchema(outputSchema);

        var xml =
            "<ListAllMyBucketsResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\">"
            + "<Buckets><Bucket><Name>assets</Name></Bucket><Bucket><Name>logs</Name></Bucket></Buckets>"
            + "</ListAllMyBucketsResult>";
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(["assets", "logs"], decoded.Buckets.Select(bucket => bucket.Name));
    }

    [Fact]
    public void XmlCodecRoundTripsNestedStructure()
    {
        var input = new Person("Ada", 36, new Address("London"));
        var expectedXml =
            "<Person><name>Ada</name><age>36</age><address><city>London</city></address></Person>";

        var addressSchema = Schemas
            .Structure<Address, AddressBuilder>(new ShapeId("example", "Address"))
            .Required(
                "city",
                static address => address.City,
                static (builder, value) => builder.City = value,
                Schemas.String
            )
            .Build(
                static () => new AddressBuilder(),
                static builder => new Address(builder.City!),
                new AddressSerializer()
            );
        var personSchema = Schemas
            .Structure<Person, PersonBuilder>(new ShapeId("example", "Person"))
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Required(
                "age",
                static person => person.Age,
                static (builder, value) => builder.Age = value,
                Schemas.Integer
            )
            .Required(
                "address",
                static person => person.Address,
                static (builder, value) => builder.Address = value,
                addressSchema
            )
            .Build(
                static () => new PersonBuilder(),
                static builder => new Person(builder.Name!, builder.Age, builder.Address!),
                new PersonSerializer()
            );
        var codec = XmlCodec.FromSchema(personSchema);

        var xml = codec.SerializeText(input);
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(expectedXml, xml);
        Assert.Equal(input, decoded);
    }
}

using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Runtime;

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
            .Build(static () => new AddressBuilder(), static builder => new Address(builder.City!));
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
                static builder => new Person(builder.Name!, builder.Age, builder.Address!)
            );
        var codec = XmlCodec.FromSchema(personSchema);

        var xml = codec.SerializeText(input);
        var decoded = codec.DeserializeText(xml);

        Assert.Equal(expectedXml, xml);
        Assert.Equal(input, decoded);
    }
}

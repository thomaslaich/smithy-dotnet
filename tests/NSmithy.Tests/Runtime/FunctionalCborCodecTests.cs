using NSmithy.Codecs.Cbor;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Runtime;

public sealed class FunctionalCborCodecTests
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
    public void FunctionalCborCodecRoundTripsNestedStructure()
    {
        var input = new Person("Ada", 36, new Address("London"));

        var addressSchema = FunctionalSchemas
            .Structure<Address, AddressBuilder>(new ShapeId("example", "Address"))
            .Required(
                "city",
                static address => address.City,
                static (builder, value) => builder.City = value,
                FunctionalSchemas.String
            )
            .Build(static () => new AddressBuilder(), static builder => new Address(builder.City!));
        var personSchema = FunctionalSchemas
            .Structure<Person, PersonBuilder>(new ShapeId("example", "Person"))
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Required(
                "age",
                static person => person.Age,
                static (builder, value) => builder.Age = value,
                FunctionalSchemas.Integer
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
        var codec = FunctionalCborCodec.FromSchema(personSchema);

        var bytes = codec.Serialize(input);
        var decoded = codec.Deserialize(bytes);

        Assert.Equal(input, decoded);
    }
}

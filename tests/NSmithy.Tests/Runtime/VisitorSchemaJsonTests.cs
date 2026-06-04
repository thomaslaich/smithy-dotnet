using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using Schema = NSmithy.Core.Schema;

namespace NSmithy.Tests.Runtime;

public sealed class VisitorSchemaJsonTests
{
    public sealed record Person(string Name, int Age, Address Address)
        : ISerializableStruct,
            IDeserializableShape<Person>
    {
        private static readonly ShapeId Id = new("example", "Person");

        private static readonly Schema NameMember = Schema.CreateMember(
            Id.WithMember("name"),
            PreludeSchemas.String
        );

        private static readonly Schema AgeMember = Schema.CreateMember(
            Id.WithMember("age"),
            PreludeSchemas.Integer
        );

        private static readonly Schema AddressMember = Schema.CreateMember(
            Id.WithMember("address"),
            Address.Schema
        );

        public static Schema Schema { get; } =
            Schema.CreateStructure(Id, [NameMember, AgeMember, AddressMember]);

        Schema ISerializableShape.Schema => Schema;

        public void Serialize(IShapeSerializer serializer) => serializer.WriteStruct(Schema, this);

        public void SerializeMembers(IShapeSerializer serializer)
        {
            serializer.WriteString(NameMember, Name);
            serializer.WriteInteger(AgeMember, Age);
            serializer.WriteStruct(AddressMember, Address);
        }

        public static Person Deserialize(IShapeDeserializer deserializer)
        {
            string? name = null;
            int age = 0;
            Address? address = null;

            deserializer.ReadStruct<object?>(
                Schema,
                state: null,
                consumer: new StructMemberConsumer<object?>(Member: ReadMember)
            );

            return new Person(name!, age, address!);

            void ReadMember(object? _, Schema member, IShapeDeserializer reader)
            {
                if (member.MemberName == "name")
                {
                    name = reader.ReadString(member);
                }
                else if (member.MemberName == "age")
                {
                    age = reader.ReadInteger(member);
                }
                else if (member.MemberName == "address")
                {
                    address = Address.Deserialize(reader);
                }
            }
        }
    }

    public sealed record Address(string City) : ISerializableStruct, IDeserializableShape<Address>
    {
        private static readonly ShapeId Id = new("example", "Address");

        private static readonly Schema CityMember = Schema.CreateMember(
            Id.WithMember("city"),
            PreludeSchemas.String
        );

        public static Schema Schema { get; } = Schema.CreateStructure(Id, [CityMember]);

        Schema ISerializableShape.Schema => Schema;

        public void Serialize(IShapeSerializer serializer) => serializer.WriteStruct(Schema, this);

        public void SerializeMembers(IShapeSerializer serializer)
        {
            serializer.WriteString(CityMember, City);
        }

        public static Address Deserialize(IShapeDeserializer deserializer)
        {
            string? city = null;

            deserializer.ReadStruct<object?>(
                Schema,
                state: null,
                consumer: new StructMemberConsumer<object?>(Member: ReadMember)
            );

            return new Address(city!);

            void ReadMember(object? _, Schema member, IShapeDeserializer reader)
            {
                if (member.MemberName == "city")
                {
                    city = reader.ReadString(member);
                }
            }
        }
    }

    [Fact]
    public void VisitorJsonCodecRoundTripsNestedStructure()
    {
        var input = new Person("Ada", 36, new Address("London"));
        var expectedJson = "{\"name\":\"Ada\",\"age\":36,\"address\":{\"city\":\"London\"}}";

        var bytes = SmithyJsonCodec.Default.Serialize(input);
        var json = Encoding.UTF8.GetString(bytes);
        var decoded = SmithyJsonCodec.Default.Deserialize<Person>(bytes);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
        Assert.Equal(ShapeKind.Structure, Person.Schema.Kind);
        Assert.Equal("address", Person.Schema.GetMember("address")?.MemberName);
    }

    public abstract record Choice : ISerializableStruct, IDeserializableShape<Choice>
    {
        private static readonly ShapeId Id = new("example", "Choice");

        private static readonly Schema StringMember = Schema.CreateMember(
            Id.WithMember("stringValue"),
            PreludeSchemas.String
        );

        private static readonly Schema IntegerMember = Schema.CreateMember(
            Id.WithMember("integerValue"),
            PreludeSchemas.Integer
        );

        public static Schema Schema { get; } =
            Schema.CreateUnion(Id, [StringMember, IntegerMember]);

        Schema ISerializableShape.Schema => Schema;

        public void Serialize(IShapeSerializer serializer) => serializer.WriteStruct(Schema, this);

        public static Choice Deserialize(IShapeDeserializer deserializer)
        {
            Choice? choice = null;
            deserializer.ReadStruct<object?>(
                Schema,
                state: null,
                consumer: new StructMemberConsumer<object?>(Member: ReadMember)
            );

            return choice ?? throw new InvalidOperationException("Union payload was empty.");

            void ReadMember(object? _, Schema member, IShapeDeserializer reader)
            {
                if (member.MemberName == "stringValue")
                {
                    choice = new StringChoice(reader.ReadString(member));
                }
                else if (member.MemberName == "integerValue")
                {
                    choice = new IntegerChoice(reader.ReadInteger(member));
                }
            }
        }

        public abstract void SerializeMembers(IShapeSerializer serializer);

        public sealed record StringChoice(string Value) : Choice
        {
            public override void SerializeMembers(IShapeSerializer serializer)
            {
                serializer.WriteString(StringMember, Value);
            }
        }

        public sealed record IntegerChoice(int Value) : Choice
        {
            public override void SerializeMembers(IShapeSerializer serializer)
            {
                serializer.WriteInteger(IntegerMember, Value);
            }
        }
    }

    [Fact]
    public void VisitorJsonCodecRoundTripsUnion()
    {
        Choice input = new Choice.StringChoice("hello");
        var expectedJson = "{\"stringValue\":\"hello\"}";

        var bytes = SmithyJsonCodec.Default.Serialize(input);
        var json = Encoding.UTF8.GetString(bytes);
        var decoded = SmithyJsonCodec.Default.Deserialize<Choice>(bytes);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
        Assert.Equal(ShapeKind.Union, Choice.Schema.Kind);
        Assert.Equal("stringValue", Choice.Schema.GetMember("stringValue")?.MemberName);
    }
}

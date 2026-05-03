using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Json;

/// <summary>
/// End-to-end smoke tests for the new visitor-based JSON codec. Uses hand-rolled shapes
/// (since the generators don't emit the visitor surface yet) to validate the
/// <see cref="IShapeSerializer"/> / <see cref="IShapeDeserializer"/> contracts before the
/// codegen migration.
/// </summary>
public sealed class SmithyJsonCodecTests
{
    private static readonly SmithyJsonCodec Codec = SmithyJsonCodec.Default;

    [Fact]
    public void RoundTripStructWithPrimitiveAndListAndMapMembers()
    {
        var input = new GreetingInput(
            Name: "Ada",
            Age: 36,
            Tags: ["mathematician", "programmer"],
            Attributes: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["city"] = "London",
                ["era"] = "Victorian",
            }
        );

        var bytes = Codec.Serialize(input);
        var json = Encoding.UTF8.GetString(bytes);

        Assert.Equal(
            """{"name":"Ada","age":36,"tags":["mathematician","programmer"],"attributes":{"city":"London","era":"Victorian"}}""",
            json
        );

        var decoded = Codec.Deserialize<GreetingInput>(bytes);
        Assert.Equal(input.Name, decoded.Name);
        Assert.Equal(input.Age, decoded.Age);
        Assert.Equal(input.Tags, decoded.Tags);
        Assert.Equal(input.Attributes, decoded.Attributes);
    }

    [Fact]
    public void HonorsJsonNameTraitOnMember()
    {
        var input = new GreetingInput(
            Name: "Grace",
            Age: 85,
            Tags: [],
            Attributes: new Dictionary<string, string>()
        );

        var json = Encoding.UTF8.GetString(Codec.Serialize(input));
        Assert.Contains("\"name\":\"Grace\"", json, StringComparison.Ordinal);

        // Round-trip a payload using the wire name to prove the deserializer also resolves via jsonName.
        var wire = """{"name":"Hopper","age":85,"tags":[],"attributes":{}}"""u8.ToArray();
        var decoded = Codec.Deserialize<GreetingInput>(wire);
        Assert.Equal("Hopper", decoded.Name);
    }

    /// <summary>
    /// Hand-rolled "structure" shape standing in for what the generators will eventually emit:
    /// a static <see cref="Schema"/>, an instance <see cref="Serialize(IShapeSerializer)"/> /
    /// <see cref="SerializeMembers(IShapeSerializer)"/>, and a static <c>Deserialize</c>.
    /// </summary>
    public sealed record GreetingInput(
        string Name,
        int Age,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, string> Attributes
    ) : ISerializableStruct, IDeserializableShape<GreetingInput>
    {
        private static readonly ShapeId Id = new("example.hello", "GreetingInput");

        // Member schemas — declared in the order the wire format should emit them.
        private static readonly Schema NameMember = Schema.CreateMember(
            Id.WithMember("name"),
            PreludeSchemas.String
        );
        private static readonly Schema AgeMember = Schema.CreateMember(
            Id.WithMember("age"),
            PreludeSchemas.Integer
        );
        private static readonly Schema TagsListSchema = Schema.CreateList(
            new ShapeId("example.hello", "TagList"),
            Schema.CreateMember(
                new ShapeId("example.hello", "TagList").WithMember("member"),
                PreludeSchemas.String
            )
        );
        private static readonly Schema TagsMember = Schema.CreateMember(
            Id.WithMember("tags"),
            TagsListSchema
        );
        private static readonly Schema AttributesMapSchema = Schema.CreateMap(
            new ShapeId("example.hello", "AttributeMap"),
            Schema.CreateMember(
                new ShapeId("example.hello", "AttributeMap").WithMember("key"),
                PreludeSchemas.String
            ),
            Schema.CreateMember(
                new ShapeId("example.hello", "AttributeMap").WithMember("value"),
                PreludeSchemas.String
            )
        );
        private static readonly Schema AttributesMember = Schema.CreateMember(
            Id.WithMember("attributes"),
            AttributesMapSchema
        );

        public static Schema Schema { get; } =
            Schema.CreateStructure(
                Id,
                members: [NameMember, AgeMember, TagsMember, AttributesMember]
            );

        Schema ISerializableShape.Schema => Schema;

        public void Serialize(IShapeSerializer serializer) => serializer.WriteStruct(Schema, this);

        public void SerializeMembers(IShapeSerializer serializer)
        {
            serializer.WriteString(NameMember, Name);
            serializer.WriteInteger(AgeMember, Age);
            serializer.WriteList(
                TagsMember,
                Tags,
                Tags.Count,
                static (tags, w) =>
                {
                    foreach (var tag in tags)
                    {
                        w.WriteString(TagsListSchema.ListMember!, tag);
                    }
                }
            );
            serializer.WriteMap(
                AttributesMember,
                Attributes,
                Attributes.Count,
                static (attrs, m) =>
                {
                    foreach (var (k, v) in attrs)
                    {
                        m.Entry(
                            k,
                            v,
                            static (val, w) => w.WriteString(AttributesMapSchema.MapValue!, val)
                        );
                    }
                }
            );
        }

        public static GreetingInput Deserialize(IShapeDeserializer deserializer)
        {
            string name = string.Empty;
            int age = 0;
            var tags = new List<string>();
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

            deserializer.ReadStruct<object?>(
                Schema,
                state: null,
                consumer: new StructMemberConsumer<object?>(Member: ReadMember)
            );

            return new GreetingInput(name, age, tags, attributes);

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
                else if (member.MemberName == "tags")
                {
                    reader.ReadList(
                        member,
                        tags,
                        (list, r) => list.Add(r.ReadString(TagsListSchema.ListMember!))
                    );
                }
                else if (member.MemberName == "attributes")
                {
                    reader.ReadMap(
                        member,
                        attributes,
                        (map, key, r) => map[key] = r.ReadString(AttributesMapSchema.MapValue!)
                    );
                }
            }
        }
    }
}

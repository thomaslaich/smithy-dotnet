using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace NSmithy.Tests.Codecs.Json;

// Focused JSON codec unit tests. The restJson1 / simpleRestJson conformance suites
// (tests/Conformance) exercise the full wire surface end to end; these tests pin the codec's
// behaviour directly for fast, debuggable iteration during development.
public sealed class JsonCodecTests
{
    // ---------------- scalars ----------------

    public sealed record Scalars(
        string Text,
        int Count,
        long Big,
        double Ratio,
        bool Flag,
        byte[] Data
    );

    public sealed class ScalarsBuilder
    {
        public string? Text { get; set; }
        public int Count { get; set; }
        public long Big { get; set; }
        public double Ratio { get; set; }
        public bool Flag { get; set; }
        public byte[]? Data { get; set; }
    }

    [Fact]
    public void JsonCodecRoundTripsScalarMembers()
    {
        var input = new Scalars("hi", 7, 9_000_000_000L, 1.5, true, [1, 2, 3]);
        var schema = Schemas
            .Structure<Scalars, ScalarsBuilder>(new ShapeId("example", "Scalars"))
            .Required("text", static s => s.Text, static (b, v) => b.Text = v, Schemas.String)
            .Required("count", static s => s.Count, static (b, v) => b.Count = v, Schemas.Integer)
            .Required("big", static s => s.Big, static (b, v) => b.Big = v, Schemas.Long)
            .Required("ratio", static s => s.Ratio, static (b, v) => b.Ratio = v, Schemas.Double)
            .Required("flag", static s => s.Flag, static (b, v) => b.Flag = v, Schemas.Boolean)
            .Required("data", static s => s.Data, static (b, v) => b.Data = v, Schemas.Blob)
            .Build(
                static () => new ScalarsBuilder(),
                static b => new Scalars(b.Text!, b.Count, b.Big, b.Ratio, b.Flag, b.Data!)
            );
        var codec = JsonCodec.FromSchema(schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        // Blobs are base64 in JSON; [1,2,3] => "AQID".
        Assert.Equal(
            "{\"text\":\"hi\",\"count\":7,\"big\":9000000000,\"ratio\":1.5,\"flag\":true,\"data\":\"AQID\"}",
            json
        );
        // Records compare byte[] by reference, so prove the round-trip by re-serializing instead.
        Assert.Equal(json, codec.SerializeText(decoded));
    }

    // ---------------- optional / absent members ----------------

    public sealed record Profile(string Name, string? Nickname);

    public sealed class ProfileBuilder
    {
        public string? Name { get; set; }
        public string? Nickname { get; set; }
    }

    private static Schema<Profile> ProfileSchema() =>
        Schemas
            .Structure<Profile, ProfileBuilder>(new ShapeId("example", "Profile"))
            .Required("name", static p => p.Name, static (b, v) => b.Name = v, Schemas.String)
            .Optional(
                "nickname",
                static p => p.Nickname!,
                static (b, v) => b.Nickname = v,
                Schemas.String
            )
            .Build(static () => new ProfileBuilder(), static b => new Profile(b.Name!, b.Nickname));

    [Fact]
    public void JsonCodecOmitsNullOptionalMember()
    {
        var codec = JsonCodec.FromSchema(ProfileSchema());

        var json = codec.SerializeText(new Profile("Ada", null));

        Assert.Equal("{\"name\":\"Ada\"}", json);
    }

    [Fact]
    public void JsonCodecDeserializesAbsentOptionalMemberAsNull()
    {
        var codec = JsonCodec.FromSchema(ProfileSchema());

        var decoded = codec.DeserializeText("{\"name\":\"Ada\"}");

        Assert.Equal(new Profile("Ada", null), decoded);
    }

    // ---------------- list + map ----------------

    public sealed record Bag(IReadOnlyList<string> Tags, IReadOnlyDictionary<string, int> Counts);

    public sealed class BagBuilder
    {
        public IReadOnlyList<string>? Tags { get; set; }
        public IReadOnlyDictionary<string, int>? Counts { get; set; }
    }

    [Fact]
    public void JsonCodecRoundTripsListAndMap()
    {
        var input = new Bag(["a", "b"], new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 });
        var schema = Schemas
            .Structure<Bag, BagBuilder>(new ShapeId("example", "Bag"))
            .Required(
                "tags",
                static b => b.Tags,
                static (b, v) => b.Tags = v,
                Schemas.List(new ShapeId("example", "Tags"), Schemas.String)
            )
            .Required(
                "counts",
                static b => b.Counts,
                static (b, v) => b.Counts = v,
                Schemas.Map(new ShapeId("example", "Counts"), Schemas.Integer)
            )
            .Build(static () => new BagBuilder(), static b => new Bag(b.Tags!, b.Counts!));
        var codec = JsonCodec.FromSchema(schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"tags\":[\"a\",\"b\"],\"counts\":{\"x\":1,\"y\":2}}", json);
        // Records compare collection members by reference, so prove the round-trip by re-serializing.
        Assert.Equal(json, codec.SerializeText(decoded));
    }

    // ---------------- nested structure ----------------

    public sealed record Address(string City);

    public sealed class AddressBuilder
    {
        public string? City { get; set; }
    }

    public sealed record Person(string Name, Address Address);

    public sealed class PersonBuilder
    {
        public string? Name { get; set; }
        public Address? Address { get; set; }
    }

    [Fact]
    public void JsonCodecRoundTripsNestedStructure()
    {
        var addressSchema = Schemas
            .Structure<Address, AddressBuilder>(new ShapeId("example", "Address"))
            .Required("city", static a => a.City, static (b, v) => b.City = v, Schemas.String)
            .Build(static () => new AddressBuilder(), static b => new Address(b.City!));
        var schema = Schemas
            .Structure<Person, PersonBuilder>(new ShapeId("example", "Person"))
            .Required("name", static p => p.Name, static (b, v) => b.Name = v, Schemas.String)
            .Required(
                "address",
                static p => p.Address,
                static (b, v) => b.Address = v,
                addressSchema
            )
            .Build(static () => new PersonBuilder(), static b => new Person(b.Name!, b.Address!));
        var codec = JsonCodec.FromSchema(schema);

        var input = new Person("Ada", new Address("London"));
        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"name\":\"Ada\",\"address\":{\"city\":\"London\"}}", json);
        Assert.Equal(input, decoded);
    }

    // ---------------- enums ----------------

    public readonly record struct Status(string Value) : IStringEnumValue<Status>
    {
        public static readonly Status Active = new("ACTIVE");

        public static Status FromValue(string value) => new(value);
    }

    public sealed record Deployment(string Name, Status Status);

    public sealed class DeploymentBuilder
    {
        public string? Name { get; set; }

        public Status Status { get; set; }
    }

    [Fact]
    public void JsonCodecRoundTripsStringEnumMember()
    {
        var input = new Deployment("deploy-api", Status.Active);
        var expectedJson = "{\"name\":\"deploy-api\",\"status\":\"ACTIVE\"}";

        var statusSchema = Schemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var deploymentSchema = Schemas
            .Structure<Deployment, DeploymentBuilder>(new ShapeId("example", "Deployment"))
            .Required(
                "name",
                static deployment => deployment.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Required(
                "status",
                static deployment => deployment.Status,
                static (builder, value) => builder.Status = value,
                statusSchema
            )
            .Build(
                static () => new DeploymentBuilder(),
                static builder => new Deployment(builder.Name!, builder.Status)
            );
        var codec = JsonCodec.FromSchema(deploymentSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }

    public enum Priority
    {
        Low = 1,
        High = 2,
    }

    public sealed record WorkItem(string Title, Priority Priority);

    public sealed class WorkItemBuilder
    {
        public string? Title { get; set; }

        public Priority Priority { get; set; }
    }

    [Fact]
    public void JsonCodecRoundTripsIntEnumMember()
    {
        var input = new WorkItem("rollback", Priority.High);
        var expectedJson = "{\"title\":\"rollback\",\"priority\":2}";

        var prioritySchema = Schemas.IntEnum<Priority>(new ShapeId("example", "Priority"));
        var workItemSchema = Schemas
            .Structure<WorkItem, WorkItemBuilder>(new ShapeId("example", "WorkItem"))
            .Required(
                "title",
                static workItem => workItem.Title,
                static (builder, value) => builder.Title = value,
                Schemas.String
            )
            .Required(
                "priority",
                static workItem => workItem.Priority,
                static (builder, value) => builder.Priority = value,
                prioritySchema
            )
            .Build(
                static () => new WorkItemBuilder(),
                static builder => new WorkItem(builder.Title!, builder.Priority)
            );
        var codec = JsonCodec.FromSchema(workItemSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }
}

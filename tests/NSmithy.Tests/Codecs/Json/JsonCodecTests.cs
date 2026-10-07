using NSmithy.Codecs.Json;
using NSmithy.Core.Serde;
using Nsmithy.Tests.Json;

namespace NSmithy.Tests.Codecs.Json;

// Focused JSON codec unit tests. The restJson1 / simpleRestJson conformance suites
// (tests/Conformance) exercise the full wire surface end to end; these tests pin the codec's
// behaviour directly for fast, debuggable iteration during development.
public sealed class JsonCodecTests
{
    // ---------------- scalars ----------------

    [Fact]
    public void JsonCodecRoundTripsScalarMembers()
    {
        var input = new Scalars(
            Text: "hi",
            Count: 7,
            Big: 9_000_000_000L,
            Ratio: 1.5,
            Flag: true,
            Data: [1, 2, 3]
        );
        var codec = JsonCodecFactory.Default.FromSchema(ScalarsSchema.Schema);

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

    [Fact]
    public void JsonCodecRoundTripsPrimitiveRootValue()
    {
        var codec = JsonCodecFactory.Default.FromSchema(Schemas.Integer);

        var json = codec.SerializeText(36);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("36", json);
        Assert.Equal(36, decoded);
    }

    // ---------------- optional / absent members ----------------

    [Fact]
    public void JsonCodecHonorsJsonNameTraitByDefault()
    {
        var codec = JsonCodecFactory.Default.FromSchema(JsonNamedProfileSchema.Schema);

        var json = codec.SerializeText(new JsonNamedProfile("Ada"));
        var decoded = codec.DeserializeText("{\"displayName\":\"Grace\"}");

        Assert.Equal("{\"displayName\":\"Ada\"}", json);
        Assert.Equal(new JsonNamedProfile("Grace"), decoded);
    }

    [Fact]
    public void JsonCodecCanUseModeledMemberNamesInsteadOfJsonNameTrait()
    {
        var codec = new JsonCodecFactory(honorJsonNameTrait: false).FromSchema(
            JsonNamedProfileSchema.Schema
        );

        var json = codec.SerializeText(new JsonNamedProfile("Ada"));
        var decoded = codec.DeserializeText("{\"name\":\"Grace\"}");

        Assert.Equal("{\"name\":\"Ada\"}", json);
        Assert.Equal(new JsonNamedProfile("Grace"), decoded);
    }

    [Fact]
    public void JsonCodecOmitsNullOptionalMember()
    {
        var codec = JsonCodecFactory.Default.FromSchema(ProfileSchema.Schema);

        var json = codec.SerializeText(new Profile("Ada"));

        Assert.Equal("{\"name\":\"Ada\"}", json);
    }

    [Fact]
    public void JsonCodecDeserializesAbsentOptionalMemberAsNull()
    {
        var codec = JsonCodecFactory.Default.FromSchema(ProfileSchema.Schema);

        var decoded = codec.DeserializeText("{\"name\":\"Ada\"}");

        Assert.Equal(new Profile("Ada"), decoded);
    }

    // ---------------- required members ----------------

    [Fact]
    public void JsonCodecRejectsMissingRequiredMember()
    {
        var codec = JsonCodecFactory.Default.FromSchema(RequiredPersonSchema.Schema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() => codec.DeserializeText("{}"));

        Assert.Equal("Missing required member 'name'.", ex.Message);
    }

    [Fact]
    public void JsonCodecRejectsNullRequiredMember()
    {
        var codec = JsonCodecFactory.Default.FromSchema(RequiredPersonSchema.Schema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() =>
            codec.DeserializeText("{\"name\":null}")
        );

        // An explicitly null required member is the same violation as an absent one, and reaches
        // the server runtime the same way.
        Assert.Equal("Missing required member 'name'.", ex.Message);
    }

    [Fact]
    public void JsonCodecReportsPathOfNestedMissingRequiredMember()
    {
        var codec = JsonCodecFactory.Default.FromSchema(OrderSchema.Schema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() =>
            codec.DeserializeText("""{"buyer":{}}""")
        );

        // The reader that finds the omission knows only "name"; the enclosing reader supplies the
        // rest as the exception unwinds.
        Assert.Equal(["buyer", "name"], ex.PathTokens);
    }

    // ---------------- list + map ----------------

    [Fact]
    public void JsonCodecRoundTripsListAndMap()
    {
        var input = new Bag(
            Tags: new Tags(["a", "b"]),
            Counts: new Counts(new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 })
        );
        var codec = JsonCodecFactory.Default.FromSchema(BagSchema.Schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"tags\":[\"a\",\"b\"],\"counts\":{\"x\":1,\"y\":2}}", json);
        // Records compare collection members by reference, so prove the round-trip by re-serializing.
        Assert.Equal(json, codec.SerializeText(decoded));
    }

    [Fact]
    public void JsonCodecAppliesListElementMemberTraits()
    {
        var codec = JsonCodecFactory.Default.FromSchema(TimelineSchema.Schema);
        var input = new Timeline(
            new Events([new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero)])
        );

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"events\":[\"2026-08-09T12:34:56Z\"]}", json);
        Assert.Equal(input.Events.Values, decoded.Events.Values);
    }

    [Fact]
    public void JsonCodecAppliesStructureMemberTraits()
    {
        var codec = JsonCodecFactory.Default.FromSchema(TimestampRecordSchema.Schema);
        var input = new TimestampRecord(new DateTimeOffset(2026, 8, 9, 12, 34, 56, TimeSpan.Zero));

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"created\":\"2026-08-09T12:34:56Z\"}", json);
        Assert.Equal(input, decoded);
    }

    // ---------------- nested structure ----------------

    [Fact]
    public void JsonCodecRoundTripsNestedStructure()
    {
        var codec = JsonCodecFactory.Default.FromSchema(PersonSchema.Schema);

        var input = new Person(Name: "Ada", Address: new Address("London"));
        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"name\":\"Ada\",\"address\":{\"city\":\"London\"}}", json);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void JsonCodecRoundTripsRecursiveStructure()
    {
        var input = new TreeNode("root", new TreeNodeList([new TreeNode("leaf")]));
        var codec = JsonCodecFactory.Default.FromSchema(TreeNodeSchema.Schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"value\":\"root\",\"children\":[{\"value\":\"leaf\"}]}", json);
        Assert.Equal(input.Value, decoded.Value);
        Assert.Equal(input.Children!.Values.Single(), decoded.Children!.Values.Single());
        Assert.Null(decoded.Children.Values.Single().Children);
    }

    // ---------------- unions ----------------

    [Fact]
    public void JsonCodecRoundTripsUnion()
    {
        Choice input = new Choice.StringValue("hello");
        var codec = JsonCodecFactory.Default.FromSchema(ChoiceSchema.Schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"stringValue\":\"hello\"}", json);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void JsonCodecRejectsUnknownUnionMember()
    {
        var codec = JsonCodecFactory.Default.FromSchema(ChoiceSchema.Schema);

        // A payload that does not match the schema is the caller's mistake, not a fault: on a server
        // the runtime turns this into a structured 400.
        var ex = Assert.Throws<MalformedRequestException>(() =>
            codec.DeserializeText("{\"missing\":\"hello\"}")
        );

        Assert.Equal(MalformedRequestKind.Serialization, ex.Kind);
        Assert.Equal("Unknown union member 'missing'.", ex.Message);
    }

    // ---------------- enums ----------------

    [Fact]
    public void JsonCodecRoundTripsStringEnumMember()
    {
        var input = new Deployment(Name: "deploy-api", Status: Status.ACTIVE);
        var codec = JsonCodecFactory.Default.FromSchema(DeploymentSchema.Schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"name\":\"deploy-api\",\"status\":\"ACTIVE\"}", json);
        Assert.Equal(input, decoded);
    }

    [Fact]
    public void JsonCodecRoundTripsIntEnumMember()
    {
        var input = new WorkItem(Title: "rollback", Priority: Priority.HIGH);
        var codec = JsonCodecFactory.Default.FromSchema(WorkItemSchema.Schema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("{\"title\":\"rollback\",\"priority\":2}", json);
        Assert.Equal(input, decoded);
    }

    // ---------------- explicit null vs absent, on a defaulted member ----------------

    // A member carrying @default always has a value in Smithy, so an explicit null
    // must materialize the default rather than leaving the member unset. The value
    // reader used to skip it, which produced an object the model says cannot exist.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"count\":null}")]
    public void DefaultedMemberIsMaterializedWhetherAbsentOrExplicitlyNull(string json)
    {
        var codec = JsonCodecFactory.Default.FromSchema(DefaultedSchema.Schema);

        Assert.Equal(7, codec.DeserializeText(json).Count);
    }

    // The projection reader is a separate code path used for shapes whose members
    // are split across the body and the HTTP envelope. It has to agree.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"count\":null}")]
    public void ProjectionReaderAgreesOnDefaultedMembers(string json)
    {
        var schema = DefaultedSchema.Schema;
        var codec = JsonCodecFactory.Default.FromProjection(Schemas.Project(schema, _ => true));

        var builder = schema.CreateTypedBuilder();
        codec.ReadInto(System.Text.Encoding.UTF8.GetBytes(json), builder);

        Assert.Equal(7, builder.Count);
    }

    [Fact]
    public void DefaultedCollectionsAreNotSharedBetweenValues()
    {
        var codec = JsonCodecFactory.Default.FromSchema(DefaultedSchema.Schema);

        var first = codec.DeserializeText("{}");
        var second = codec.DeserializeText("{}");

        Assert.Empty(first.Labels!.Values);
        Assert.NotSame(first.Labels, second.Labels);
    }

    // A response writes a member's default when its value is absent; a request body leaves it
    // out, so the receiver applies its own.
    [Theory]
    [InlineData(true, "{\"count\":7,\"labels\":[]}")]
    [InlineData(false, "{}")]
    public void AbsentDefaultedMemberIsWrittenOnlyWhenDefaultsAreMaterialized(
        bool materialize,
        string expected
    )
    {
        var codec = JsonCodecFactory.Default.FromSchema(
            DefaultedSchema.Schema,
            new CodecFactoryOptions { MaterializeTopLevelDefaults = materialize }
        );

        Assert.Equal(expected, codec.SerializeText(new Defaulted()));
    }
}

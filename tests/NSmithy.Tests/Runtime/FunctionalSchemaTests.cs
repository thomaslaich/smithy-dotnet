using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Functional;
using NSmithy.Protocols.Rest;

namespace NSmithy.Tests.Runtime;

public sealed class FunctionalSchemaTests
{
    public sealed record Person(string Name, int Age, Address Address);

    public sealed record Address(string City);

    public sealed class PersonBuilder
    {
        public string? Name { get; set; }

        public int Age { get; set; }

        public Address? Address { get; set; }
    }

    public sealed class AddressBuilder
    {
        public string? City { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsNestedStructure()
    {
        var input = new Person("Ada", 36, new Address("London"));
        var expectedJson = "{\"name\":\"Ada\",\"age\":36,\"address\":{\"city\":\"London\"}}";

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

        var personCodec = FunctionalJsonCodec.FromSchema(personSchema);

        var json = personCodec.Serialize(input);
        var decoded = personCodec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
        Assert.Equal(ShapeKind.Structure, personSchema.Kind);
        Assert.Equal("address", personSchema.GetMember("address")?.Name);
    }

    public sealed record VisitorInput(string Name, int Age);

    public sealed class VisitorInputBuilder
    {
        public string? Name { get; set; }

        public int Age { get; set; }
    }

    [Fact]
    public void FunctionalStructSchemaVisitsMembersWithTypedAccessors()
    {
        var input = new VisitorInput("Ada", 36);
        var schema = FunctionalSchemas
            .Structure<VisitorInput, VisitorInputBuilder>(new ShapeId("example", "VisitorInput"))
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Required(
                "age",
                static value => value.Age,
                static (builder, value) => builder.Age = value,
                FunctionalSchemas.Integer
            )
            .Build(
                static () => new VisitorInputBuilder(),
                static builder => new VisitorInput(builder.Name!, builder.Age)
            );
        var visitor = new VisitorInputMemberVisitor(input);

        schema.VisitMembers(visitor);

        Assert.Equal(["name:String:Ada", "age:Int32:36"], visitor.Visited);
    }

    private sealed class VisitorInputMemberVisitor(VisitorInput input)
        : IFunctionalMemberVisitor<VisitorInput>
    {
        public List<string> Visited { get; } = [];

        public void Visit<TValue>(IFunctionalMemberSchema<VisitorInput, TValue> member)
        {
            Visited.Add($"{member.Name}:{typeof(TValue).Name}:{member.GetValue(input)}");
        }
    }

    public sealed record TreeNode(string Value, IReadOnlyList<TreeNode>? Children = null);

    public sealed class TreeNodeBuilder
    {
        public string? Value { get; set; }

        public IReadOnlyList<TreeNode>? Children { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsRecursiveSchemaWithLazyReference()
    {
        var input = new TreeNode("root", [new TreeNode("leaf")]);
        var expectedJson = "{\"value\":\"root\",\"children\":[{\"value\":\"leaf\"}]}";

        FunctionalStructSchema<TreeNode, TreeNodeBuilder>? treeSchema = null;
        var childrenSchema = FunctionalSchemas.List(
            new ShapeId("example", "TreeNodeList"),
            FunctionalSchemas.Lazy(() => treeSchema!)
        );
        treeSchema = FunctionalSchemas
            .Structure<TreeNode, TreeNodeBuilder>(new ShapeId("example", "TreeNode"))
            .Required(
                "value",
                static value => value.Value,
                static (builder, value) => builder.Value = value,
                FunctionalSchemas.String
            )
            .Optional(
                "children",
                static value => value.Children!,
                static (builder, value) => builder.Children = value,
                childrenSchema
            )
            .Build(
                static () => new TreeNodeBuilder(),
                static builder => new TreeNode(builder.Value!, builder.Children)
            );
        var codec = FunctionalJsonCodec.FromSchema(treeSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input.Value, decoded.Value);
        Assert.Equal(input.Children!.Single().Value, decoded.Children!.Single().Value);
        Assert.Null(decoded.Children!.Single().Children);
        Assert.Equal(new ShapeId("example", "TreeNode"), childrenSchema.Element.Id);
    }

    public sealed record CollectionInput(
        string Name,
        IReadOnlyList<string> Tags,
        IReadOnlySet<string> Aliases,
        IReadOnlyDictionary<string, int> Scores,
        string? Nickname
    );

    public sealed class CollectionInputBuilder
    {
        public string? Name { get; set; }

        public IReadOnlyList<string>? Tags { get; set; }

        public IReadOnlySet<string>? Aliases { get; set; }

        public IReadOnlyDictionary<string, int>? Scores { get; set; }

        public string? Nickname { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsCollectionsAndOptionalMembers()
    {
        var input = new CollectionInput(
            "Ada",
            ["mathematician", "programmer"],
            new SortedSet<string>(["analyst", "programmer"], StringComparer.Ordinal),
            new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["logic"] = 10,
                ["math"] = 9,
            },
            Nickname: null
        );
        var expectedJson =
            "{\"name\":\"Ada\",\"tags\":[\"mathematician\",\"programmer\"],\"aliases\":[\"analyst\",\"programmer\"],\"scores\":{\"logic\":10,\"math\":9}}";

        var tagListSchema = FunctionalSchemas.List(
            new ShapeId("example", "TagList"),
            FunctionalSchemas.String
        );
        var aliasSetSchema = FunctionalSchemas.Set(
            new ShapeId("example", "AliasSet"),
            FunctionalSchemas.String
        );
        var scoresSchema = FunctionalSchemas.Map(
            new ShapeId("example", "Scores"),
            FunctionalSchemas.Integer
        );
        var inputSchema = FunctionalSchemas
            .Structure<CollectionInput, CollectionInputBuilder>(
                new ShapeId("example", "CollectionInput")
            )
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Required(
                "tags",
                static value => value.Tags,
                static (builder, value) => builder.Tags = value,
                tagListSchema
            )
            .Required(
                "aliases",
                static value => value.Aliases,
                static (builder, value) => builder.Aliases = value,
                aliasSetSchema
            )
            .Required(
                "scores",
                static value => value.Scores,
                static (builder, value) => builder.Scores = value,
                scoresSchema
            )
            .Optional(
                "nickname",
                static value => value.Nickname!,
                static (builder, value) => builder.Nickname = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new CollectionInputBuilder(),
                static builder => new CollectionInput(
                    builder.Name!,
                    builder.Tags!,
                    builder.Aliases!,
                    builder.Scores!,
                    builder.Nickname
                )
            );
        var codec = FunctionalJsonCodec.FromSchema(inputSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input.Name, decoded.Name);
        Assert.Equal(input.Tags, decoded.Tags);
        Assert.True(input.Aliases.SetEquals(decoded.Aliases));
        Assert.Equal(input.Scores, decoded.Scores);
        Assert.Null(decoded.Nickname);
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsPrimitiveRootValue()
    {
        var codec = FunctionalJsonCodec.FromSchema(FunctionalSchemas.Integer);

        var json = codec.Serialize(36);
        var decoded = codec.Deserialize(json);

        Assert.Equal("36", json);
        Assert.Equal(36, decoded);
    }

    public readonly record struct Status(string Value) : IFunctionalStringEnumValue<Status>
    {
        public static readonly Status Active = new("ACTIVE");

        public static readonly Status Inactive = new("INACTIVE");

        public static Status FromValue(string value) => new(value);
    }

    public sealed record Job(string Name, Status Status);

    public sealed class JobBuilder
    {
        public string? Name { get; set; }

        public Status Status { get; set; }
    }

    [Fact]
    public void FunctionalStringEnumSchemaModelsEnumWireValue()
    {
        var statusSchema = FunctionalSchemas.StringEnum<Status>(new ShapeId("example", "Status"));

        Assert.Equal(new ShapeId("example", "Status"), statusSchema.Id);
        Assert.Equal(ShapeKind.Enum, statusSchema.Kind);
        Assert.Equal("ACTIVE", ((IFunctionalStringEnumValue)Status.Active).Value);
        Assert.Equal(Status.Inactive, statusSchema.Create("INACTIVE"));
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsStringEnumSchema()
    {
        var statusSchema = FunctionalSchemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var codec = FunctionalJsonCodec.FromSchema(statusSchema);

        var json = codec.Serialize(Status.Active);
        var decoded = codec.Deserialize(json);

        Assert.Equal("\"ACTIVE\"", json);
        Assert.Equal(Status.Active, decoded);
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsStructureWithStringEnumMember()
    {
        var input = new Job("deploy", Status.Active);
        var expectedJson = "{\"name\":\"deploy\",\"status\":\"ACTIVE\"}";

        var statusSchema = FunctionalSchemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var jobSchema = FunctionalSchemas
            .Structure<Job, JobBuilder>(new ShapeId("example", "Job"))
            .Required(
                "name",
                static job => job.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Required(
                "status",
                static job => job.Status,
                static (builder, value) => builder.Status = value,
                statusSchema
            )
            .Build(
                static () => new JobBuilder(),
                static builder => new Job(builder.Name!, builder.Status)
            );
        var codec = FunctionalJsonCodec.FromSchema(jobSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }

    public abstract record Choice
    {
        public sealed record StringChoice(string Value) : Choice;

        public sealed record IntegerChoice(int Value) : Choice;
    }

    [Fact]
    public void FunctionalJsonCodecRoundTripsUnion()
    {
        Choice input = new Choice.StringChoice("hello");
        var expectedJson = "{\"stringValue\":\"hello\"}";
        var choiceSchema = FunctionalSchemas
            .Union<Choice>(new ShapeId("example", "Choice"))
            .Case(
                "stringValue",
                static choice => choice is Choice.StringChoice,
                static choice => ((Choice.StringChoice)choice).Value,
                static value => new Choice.StringChoice(value),
                FunctionalSchemas.String
            )
            .Case(
                "integerValue",
                static choice => choice is Choice.IntegerChoice,
                static choice => ((Choice.IntegerChoice)choice).Value,
                static value => new Choice.IntegerChoice(value),
                FunctionalSchemas.Integer
            )
            .Build();
        var codec = FunctionalJsonCodec.FromSchema(choiceSchema);

        var json = codec.Serialize(input);
        var decoded = codec.Deserialize(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
        Assert.Equal(ShapeKind.Union, choiceSchema.Kind);
        Assert.Equal("stringValue", choiceSchema.GetCase("stringValue")?.Name);
    }

    [Fact]
    public void FunctionalJsonCodecRejectsUnknownUnionMember()
    {
        var choiceSchema = FunctionalSchemas
            .Union<Choice>(new ShapeId("example", "Choice"))
            .Case(
                "stringValue",
                static choice => choice is Choice.StringChoice,
                static choice => ((Choice.StringChoice)choice).Value,
                static value => new Choice.StringChoice(value),
                FunctionalSchemas.String
            )
            .Build();
        var codec = FunctionalJsonCodec.FromSchema(choiceSchema);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            codec.Deserialize("{\"missing\":\"hello\"}")
        );

        Assert.Equal("Unknown union member 'missing'.", ex.Message);
    }

    public sealed record RequiredPerson(string Name);

    public sealed class RequiredPersonBuilder
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void FunctionalJsonCodecRejectsMissingRequiredMember()
    {
        var schema = FunctionalSchemas
            .Structure<RequiredPerson, RequiredPersonBuilder>(
                new ShapeId("example", "RequiredPerson")
            )
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new RequiredPersonBuilder(),
                static builder => new RequiredPerson(builder.Name!)
            );
        var codec = FunctionalJsonCodec.FromSchema(schema);

        var ex = Assert.Throws<InvalidOperationException>(() => codec.Deserialize("{}"));

        Assert.Equal("Missing required member 'name'.", ex.Message);
    }

    [Fact]
    public void FunctionalJsonCodecRejectsNullRequiredMember()
    {
        var schema = FunctionalSchemas
            .Structure<RequiredPerson, RequiredPersonBuilder>(
                new ShapeId("example", "RequiredPerson")
            )
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new RequiredPersonBuilder(),
                static builder => new RequiredPerson(builder.Name!)
            );
        var codec = FunctionalJsonCodec.FromSchema(schema);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            codec.Deserialize("{\"name\":null}")
        );

        Assert.Equal("Required member 'name' cannot be null.", ex.Message);
    }

    public sealed record UpdateUserInput(string UserId, string? RequestToken, string DisplayName);

    public sealed class UpdateUserInputBuilder
    {
        public string? UserId { get; set; }

        public string? RequestToken { get; set; }

        public string? DisplayName { get; set; }
    }

    public sealed record UpdateUserOutput;

    public sealed class UpdateUserOutputBuilder { }

    [Fact]
    public void FunctionalOperationSchemaCarriesOperationAndMemberTraits()
    {
        var inputSchema = FunctionalSchemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                FunctionalSchemas.String,
                traits: [FunctionalRestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = FunctionalSchemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = FunctionalSchemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [FunctionalRestTraits.HttpTrait("PUT", "/users/{userId}")]
        );

        Assert.Equal(ShapeKind.Operation, operation.Kind);
        Assert.Same(inputSchema, operation.Input);
        Assert.Same(outputSchema, operation.Output);
        Assert.Equal(
            "PUT",
            operation
                .GetTrait(FunctionalRestTraits.Http)!
                .Value.Value.AsObject()["method"]
                .AsString()
        );
        Assert.True(
            inputSchema.GetMember("userId")!.Traits.ContainsKey(FunctionalRestTraits.HttpLabel)
        );
        Assert.Equal(
            "X-Request-Token",
            inputSchema
                .GetMember("requestToken")!
                .Traits[FunctionalRestTraits.HttpHeader]
                .Value.AsString()
        );
        Assert.False(
            inputSchema.GetMember("displayName")!.Traits.ContainsKey(FunctionalRestTraits.HttpLabel)
        );
    }
}

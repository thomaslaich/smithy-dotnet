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

using System.Text;
using NSmithy.Codecs.Json;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;

namespace NSmithy.Tests.Core;

public sealed class SchemaTests
{
    public sealed record Person(string Name, int Age, Address Address);

    public sealed record Address(string City);

    public sealed class TestException(string? message) : Exception(message);

    public sealed class TestExceptionBuilder
    {
        public string? Message { get; set; }
    }

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
    public void JsonCodecRoundTripsNestedStructure()
    {
        var input = new Person("Ada", 36, new Address("London"));
        var expectedJson = "{\"name\":\"Ada\",\"age\":36,\"address\":{\"city\":\"London\"}}";

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

        var personCodec = JsonCodecFactory.Default.FromSchema(personSchema);

        var json = personCodec.SerializeText(input);
        var decoded = personCodec.DeserializeText(json);

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
    public void StructSchemaVisitsMembersWithTypedAccessors()
    {
        var input = new VisitorInput("Ada", 36);
        var schema = Schemas
            .Structure<VisitorInput, VisitorInputBuilder>(new ShapeId("example", "VisitorInput"))
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Required(
                "age",
                static value => value.Age,
                static (builder, value) => builder.Age = value,
                Schemas.Integer
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
        : IMemberVisitor<VisitorInput>
    {
        public List<string> Visited { get; } = [];

        public void Visit<TValue>(IMemberSchema<VisitorInput, TValue> member)
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
    public void JsonCodecRoundTripsRecursiveSchemaWithLazyReference()
    {
        var input = new TreeNode("root", [new TreeNode("leaf")]);
        var expectedJson = "{\"value\":\"root\",\"children\":[{\"value\":\"leaf\"}]}";

        StructSchema<TreeNode, TreeNodeBuilder>? treeSchema = null;
        var childrenSchema = Schemas.List(
            new ShapeId("example", "TreeNodeList"),
            Schemas.Lazy(() => treeSchema!)
        );
        treeSchema = Schemas
            .Structure<TreeNode, TreeNodeBuilder>(new ShapeId("example", "TreeNode"))
            .Required(
                "value",
                static value => value.Value,
                static (builder, value) => builder.Value = value,
                Schemas.String
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
        var codec = JsonCodecFactory.Default.FromSchema(treeSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

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
    public void JsonCodecRoundTripsCollectionsAndOptionalMembers()
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

        var tagListSchema = Schemas.List(new ShapeId("example", "TagList"), Schemas.String);
        var aliasSetSchema = Schemas.Set(new ShapeId("example", "AliasSet"), Schemas.String);
        var scoresSchema = Schemas.Map(new ShapeId("example", "Scores"), Schemas.Integer);
        var inputSchema = Schemas
            .Structure<CollectionInput, CollectionInputBuilder>(
                new ShapeId("example", "CollectionInput")
            )
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
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
                Schemas.String
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
        var codec = JsonCodecFactory.Default.FromSchema(inputSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input.Name, decoded.Name);
        Assert.Equal(input.Tags, decoded.Tags);
        Assert.True(input.Aliases.SetEquals(decoded.Aliases));
        Assert.Equal(input.Scores, decoded.Scores);
        Assert.Null(decoded.Nickname);
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

    public readonly record struct Status(string Value) : IStringEnumValue<Status>
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
    public void StringEnumSchemaModelsEnumWireValue()
    {
        var statusSchema = Schemas.StringEnum<Status>(new ShapeId("example", "Status"));

        Assert.Equal(new ShapeId("example", "Status"), statusSchema.Id);
        Assert.Equal(ShapeKind.Enum, statusSchema.Kind);
        Assert.Equal("ACTIVE", ((IStringEnumValue)Status.Active).Value);
        Assert.Equal(Status.Inactive, statusSchema.Create("INACTIVE"));
    }

    [Fact]
    public void JsonCodecRoundTripsStringEnumSchema()
    {
        var statusSchema = Schemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var codec = JsonCodecFactory.Default.FromSchema(statusSchema);

        var json = codec.SerializeText(Status.Active);
        var decoded = codec.DeserializeText(json);

        Assert.Equal("\"ACTIVE\"", json);
        Assert.Equal(Status.Active, decoded);
    }

    [Fact]
    public void JsonCodecRoundTripsStructureWithStringEnumMember()
    {
        var input = new Job("deploy", Status.Active);
        var expectedJson = "{\"name\":\"deploy\",\"status\":\"ACTIVE\"}";

        var statusSchema = Schemas.StringEnum<Status>(new ShapeId("example", "Status"));
        var jobSchema = Schemas
            .Structure<Job, JobBuilder>(new ShapeId("example", "Job"))
            .Required(
                "name",
                static job => job.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
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
        var codec = JsonCodecFactory.Default.FromSchema(jobSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
    }

    public abstract record Choice
    {
        public sealed record StringChoice(string Value) : Choice;

        public sealed record IntegerChoice(int Value) : Choice;
    }

    [Fact]
    public void JsonCodecRoundTripsUnion()
    {
        Choice input = new Choice.StringChoice("hello");
        var expectedJson = "{\"stringValue\":\"hello\"}";
        var choiceSchema = Schemas
            .Union<Choice>(new ShapeId("example", "Choice"))
            .Case(
                "stringValue",
                static choice => choice is Choice.StringChoice,
                static choice => ((Choice.StringChoice)choice).Value,
                static value => new Choice.StringChoice(value),
                Schemas.String
            )
            .Case(
                "integerValue",
                static choice => choice is Choice.IntegerChoice,
                static choice => ((Choice.IntegerChoice)choice).Value,
                static value => new Choice.IntegerChoice(value),
                Schemas.Integer
            )
            .Build();
        var codec = JsonCodecFactory.Default.FromSchema(choiceSchema);

        var json = codec.SerializeText(input);
        var decoded = codec.DeserializeText(json);

        Assert.Equal(expectedJson, json);
        Assert.Equal(input, decoded);
        Assert.Equal(ShapeKind.Union, choiceSchema.Kind);
        Assert.Equal("stringValue", choiceSchema.GetCase("stringValue")?.Name);
    }

    [Fact]
    public void JsonCodecRejectsUnknownUnionMember()
    {
        var choiceSchema = Schemas
            .Union<Choice>(new ShapeId("example", "Choice"))
            .Case(
                "stringValue",
                static choice => choice is Choice.StringChoice,
                static choice => ((Choice.StringChoice)choice).Value,
                static value => new Choice.StringChoice(value),
                Schemas.String
            )
            .Build();
        var codec = JsonCodecFactory.Default.FromSchema(choiceSchema);

        // A payload that does not match the schema is the caller's mistake, not a fault: on a server
        // the runtime turns this into a structured 400.
        var ex = Assert.Throws<MalformedRequestException>(() =>
            codec.DeserializeText("{\"missing\":\"hello\"}")
        );

        Assert.Equal(MalformedRequestKind.Serialization, ex.Kind);
        Assert.Equal("Unknown union member 'missing'.", ex.Message);
    }

    public sealed record RequiredPerson(string Name);

    public sealed class RequiredPersonBuilder
    {
        public string? Name { get; set; }
    }

    public sealed record Order(Customer Buyer);

    public sealed class OrderBuilder
    {
        public Customer? Buyer { get; set; }
    }

    public sealed record Customer(string Name);

    public sealed class CustomerBuilder
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void JsonCodecReportsPathOfNestedMissingRequiredMember()
    {
        var customerSchema = Schemas
            .Structure<Customer, CustomerBuilder>(new ShapeId("example", "Customer"))
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Build(
                static () => new CustomerBuilder(),
                static builder => new Customer(builder.Name!)
            );
        var orderSchema = Schemas
            .Structure<Order, OrderBuilder>(new ShapeId("example", "Order"))
            .Required(
                "buyer",
                static value => value.Buyer,
                static (builder, value) => builder.Buyer = value,
                customerSchema
            )
            .Build(static () => new OrderBuilder(), static builder => new Order(builder.Buyer!));
        var codec = JsonCodecFactory.Default.FromSchema(orderSchema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() =>
            codec.DeserializeText("""{"buyer":{}}""")
        );

        // The reader that finds the omission knows only "name"; the enclosing reader supplies the
        // rest as the exception unwinds.
        Assert.Equal(["buyer", "name"], ex.PathTokens);
    }

    [Fact]
    public void JsonCodecRejectsMissingRequiredMember()
    {
        var schema = Schemas
            .Structure<RequiredPerson, RequiredPersonBuilder>(
                new ShapeId("example", "RequiredPerson")
            )
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Build(
                static () => new RequiredPersonBuilder(),
                static builder => new RequiredPerson(builder.Name!)
            );
        var codec = JsonCodecFactory.Default.FromSchema(schema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() => codec.DeserializeText("{}"));

        Assert.Equal("Missing required member 'name'.", ex.Message);
    }

    [Fact]
    public void JsonCodecRejectsNullRequiredMember()
    {
        var schema = Schemas
            .Structure<RequiredPerson, RequiredPersonBuilder>(
                new ShapeId("example", "RequiredPerson")
            )
            .Required(
                "name",
                static person => person.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Build(
                static () => new RequiredPersonBuilder(),
                static builder => new RequiredPerson(builder.Name!)
            );
        var codec = JsonCodecFactory.Default.FromSchema(schema);

        var ex = Assert.Throws<MissingRequiredMemberException>(() =>
            codec.DeserializeText("{\"name\":null}")
        );

        // An explicitly null required member is the same violation as an absent one, and reaches
        // the server runtime the same way.
        Assert.Equal("Missing required member 'name'.", ex.Message);
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
    public void OperationSchemaCarriesOperationAndMemberTraits()
    {
        var inputSchema = Schemas
            .Structure<UpdateUserInput, UpdateUserInputBuilder>(
                new ShapeId("example", "UpdateUserInput")
            )
            .Required(
                "userId",
                static input => input.UserId,
                static (builder, value) => builder.UserId = value,
                Schemas.String,
                traits: [RestTraits.HttpLabelTrait]
            )
            .Optional(
                "requestToken",
                static input => input.RequestToken!,
                static (builder, value) => builder.RequestToken = value,
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Request-Token")]
            )
            .Required(
                "displayName",
                static input => input.DisplayName,
                static (builder, value) => builder.DisplayName = value,
                Schemas.String
            )
            .Build(
                static () => new UpdateUserInputBuilder(),
                static builder => new UpdateUserInput(
                    builder.UserId!,
                    builder.RequestToken,
                    builder.DisplayName!
                )
            );
        var outputSchema = Schemas
            .Structure<UpdateUserOutput, UpdateUserOutputBuilder>(
                new ShapeId("example", "UpdateUserOutput")
            )
            .Build(static () => new UpdateUserOutputBuilder(), static _ => new UpdateUserOutput());
        var operation = Schemas.Operation(
            new ShapeId("example", "UpdateUser"),
            inputSchema,
            outputSchema,
            traits: [RestTraits.HttpTrait("PUT", "/users/{userId}")]
        );

        Assert.Equal(ShapeKind.Operation, operation.Kind);
        Assert.Same(inputSchema, operation.Input);
        Assert.Same(outputSchema, operation.Output);
        Assert.False(operation.IsStreaming);
        Assert.Equal(
            "PUT",
            operation.GetTrait(RestTraits.Http)!.Value.Value.AsObject()["method"].AsString()
        );
        Assert.True(
            inputSchema.GetMember("userId")!.MemberTraits.ContainsKey(RestTraits.HttpLabel)
        );
        Assert.Equal(
            "X-Request-Token",
            inputSchema
                .GetMember("requestToken")!
                .MemberTraits[RestTraits.HttpHeader]
                .Value.AsString()
        );
        Assert.False(
            inputSchema.GetMember("displayName")!.MemberTraits.ContainsKey(RestTraits.HttpLabel)
        );
    }

    [Fact]
    public void MemberSchemaResolvesMemberTraitsBeforeTargetTraits()
    {
        var traitId = ShapeId.Parse("smithy.api#timestampFormat");
        var target = Schemas.TimestampWithTraits([new Trait(traitId, Document.From("date-time"))]);
        var schema = Schemas
            .Structure<DateInput, DateInputBuilder>(new ShapeId("example", "DateInput"))
            .Required(
                "created",
                static input => input.Created,
                static (builder, value) => builder.Created = value,
                target,
                [new Trait(traitId, Document.From("epoch-seconds"))]
            )
            .Build(
                static () => new DateInputBuilder(),
                static builder => new DateInput(builder.Created)
            );

        var member = schema.GetMember("created")!;

        Assert.Equal("epoch-seconds", member.GetTrait(traitId)!.Value.Value.AsString());
        Assert.Equal("epoch-seconds", member.MemberTraits[traitId].Value.AsString());
        Assert.Equal("date-time", member.Target.GetTrait(traitId)!.Value.Value.AsString());
    }

    public sealed record DateInput(DateTimeOffset Created);

    public sealed class DateInputBuilder
    {
        public DateTimeOffset Created { get; set; }
    }

    [Fact]
    public void OperationSchemaCarriesModeledErrors()
    {
        var errorSchema = Schemas
            .Structure<TestException, TestExceptionBuilder>(new ShapeId("example", "BadRequest"))
            .Optional(
                "message",
                static error => error.Message,
                static (builder, value) => builder.Message = value,
                Schemas.String
            )
            .Build(
                static () => new TestExceptionBuilder(),
                static builder => new TestException(builder.Message)
            );

        var error = Schemas.OperationError(new ShapeId("example", "BadRequest"), errorSchema, 400);
        var operation = Schemas.Operation(
            new ShapeId("example", "GetUser"),
            Schemas.Unit,
            Schemas.Unit,
            [error]
        );

        Assert.Same(error, operation.Errors[0]);
        Assert.Same(errorSchema, ((OperationErrorSchema<TestException>)operation.Errors[0]).Schema);
        Assert.Equal(400, operation.Errors[0].HttpStatusCode);
    }

    [Fact]
    public void StructProjectionSnapshotsSelectedMembers()
    {
        var schema = Schemas
            .Structure<VisitorInput, VisitorInputBuilder>(new ShapeId("example", "VisitorInput"))
            .Required(
                "name",
                static value => value.Name,
                static (builder, value) => builder.Name = value,
                Schemas.String
            )
            .Required(
                "age",
                static value => value.Age,
                static (builder, value) => builder.Age = value,
                Schemas.Integer
            )
            .Build(
                static () => new VisitorInputBuilder(),
                static builder => new VisitorInput(builder.Name!, builder.Age)
            );
        var selected = new HashSet<string>(StringComparer.Ordinal) { "name" };
        var projection = Schemas.Project(schema, selected);

        selected.Clear();
        selected.Add("age");

        Assert.NotNull(projection.GetMember("name"));
        Assert.Null(projection.GetMember("age"));
        Assert.Equal(
            "{\"name\":\"Ada\"}",
            Encoding.UTF8.GetString(
                JsonCodecFactory
                    .Default.FromProjection(projection)
                    .Serialize(new VisitorInput("Ada", 36))
            )
        );
    }

    [Fact]
    public void SchemaVisitorsRecoverHiddenBuilderAndErrorTypes()
    {
        var structure = Schemas
            .Structure<VisitorInput, VisitorInputBuilder>(new ShapeId("example", "VisitorInput"))
            .Build(
                static () => new VisitorInputBuilder(),
                static builder => new VisitorInput(builder.Name!, builder.Age)
            );
        var error = Schemas.OperationError(
            new ShapeId("example", "BadRequest"),
            Schemas
                .Structure<TestException, TestExceptionBuilder>(
                    new ShapeId("example", "BadRequest")
                )
                .Build(
                    static () => new TestExceptionBuilder(),
                    static builder => new TestException(builder.Message)
                ),
            400
        );

        Assert.Equal(typeof(VisitorInputBuilder), GetBuilderType(structure));
        Assert.Equal(typeof(TestException), GetErrorType(error));
    }

    [Fact]
    public void CompiledCollectionDefaultsDoNotAlias()
    {
        var defaultId = ShapeId.Parse("smithy.api#default");
        var schema = Schemas.List(new ShapeId("example", "Names"), Schemas.String);
        IReadOnlyDictionary<ShapeId, Trait> traits = new Dictionary<ShapeId, Trait>
        {
            [defaultId] = new(defaultId, Document.From([Document.From("Ada")])),
        };

        Assert.True(
            DefaultValues.TryCompile(schema, traits, honorClientOptional: false, out var create)
        );

        var first = create();
        var second = create();
        Assert.NotSame(first, second);
        Assert.Equal(["Ada"], first);
        Assert.Equal(["Ada"], second);
    }

    [Fact]
    public void NullableSchemaDistinguishesTypedAndUntypedTargets()
    {
        var nullable = Assert.IsType<NullableSchema<int>>(Schemas.Nullable(Schemas.Integer));

        Assert.Same(Schemas.Integer, nullable.TypedTarget);
        Assert.Same(Schemas.Integer, nullable.Target);
    }

    private sealed class BuilderTypeVisitor : IStructSchemaVisitor<VisitorInput, Type>
    {
        public Type Visit<TBuilder>(IStructSchema<VisitorInput, TBuilder> schema) =>
            typeof(TBuilder);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The erased interface is the behavior under test."
    )]
    private static Type GetBuilderType(IStructSchema<VisitorInput> schema) =>
        schema.Accept(new BuilderTypeVisitor());

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "The erased interface is the behavior under test."
    )]
    private static Type GetErrorType(IOperationErrorSchema schema) =>
        schema.Accept(ErrorTypeVisitor.Instance);

    private sealed class ErrorTypeVisitor : IOperationErrorSchemaVisitor<Type>
    {
        public static ErrorTypeVisitor Instance { get; } = new();

        public Type Visit<TError>(OperationErrorSchema<TError> schema)
            where TError : Exception => typeof(TError);
    }
}

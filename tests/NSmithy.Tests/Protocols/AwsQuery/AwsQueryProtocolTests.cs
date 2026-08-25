using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.AwsQuery;

namespace NSmithy.Tests.Protocols.AwsQuery;

public sealed class AwsQueryProtocolTests
{
    private static readonly Trait XmlFlattened = new(ShapeId.Parse("smithy.api#xmlFlattened"));

    public sealed record Nested(string? Value);

    public sealed class NestedBuilder
    {
        public string? Value { get; set; }
    }

    public sealed record QueryInput(
        string? Text = null,
        DateTimeOffset? When = null,
        IReadOnlyList<string>? Items = null,
        IReadOnlyList<string>? FlatItems = null,
        IReadOnlyDictionary<string, string>? Tags = null,
        Nested? Nested = null
    );

    public sealed class QueryInputBuilder
    {
        public string? Text { get; set; }
        public DateTimeOffset? When { get; set; }
        public IReadOnlyList<string>? Items { get; set; }
        public IReadOnlyList<string>? FlatItems { get; set; }
        public IReadOnlyDictionary<string, string>? Tags { get; set; }
        public Nested? Nested { get; set; }
    }

    public sealed record GreetingOutput(string? Greeting = null);

    public sealed class GreetingOutputBuilder
    {
        public string? Greeting { get; set; }
    }

    public sealed class GreetingException(string? message) : Exception(message)
    {
        public string? Detail { get; init; }
    }

    public sealed class GreetingErrorBuilder
    {
        public string? Message { get; set; }
        public string? Detail { get; set; }
    }

    [Fact]
    public void AwsQuerySerializesOfficialFormLayout()
    {
        var operation = Operation("SendGreeting");
        var protocol = Bind<AwsQueryProtocol>(operation);
        var input = new QueryInput(
            Text: "hello world",
            When: DateTimeOffset.Parse("2015-01-25T08:00:00Z"),
            Items: ["a", "b"],
            FlatItems: ["c", "d"],
            Tags: new Dictionary<string, string> { ["first"] = "1", ["second"] = "2" },
            Nested: new Nested("inside")
        );

        var request = protocol.SerializeRequest(input);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/", request.RequestUri);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Equal("text/xml", request.Headers["Accept"].Single());
        Assert.Equal(
            "Action=SendGreeting&Version=2020-01-08&Text=hello%20world&When=2015-01-25T08%3A00%3A00Z&Items.member.1=a&Items.member.2=b&FlatItems.1=c&FlatItems.2=d&Tags.entry.1.key=first&Tags.entry.1.value=1&Tags.entry.2.key=second&Tags.entry.2.value=2&Nested.Value=inside",
            BodyText(request)
        );
    }

    [Fact]
    public void Ec2QueryUppercasesNamesAndAlwaysFlattensLists()
    {
        var operation = Operation("SendGreeting");
        var protocol = Bind<Ec2QueryProtocol>(operation);
        var input = new QueryInput(Text: "hello", Items: ["a", "b"], Nested: new Nested("v"));

        var request = protocol.SerializeRequest(input);

        Assert.Equal(
            "Action=SendGreeting&Version=2020-01-08&Text=hello&Items.1=a&Items.2=b&Nested.Value=v",
            BodyText(request)
        );
    }

    [Fact]
    public async Task AwsQueryDeserializesTheResultWrapper()
    {
        var protocol = Bind<AwsQueryProtocol>(Operation("SendGreeting"));
        var response = Response(
            HttpStatusCode.OK,
            """
            <SendGreetingResponse xmlns="https://example.com/">
              <SendGreetingResult><Greeting>Hello</Greeting></SendGreetingResult>
              <ResponseMetadata><RequestId>abc</RequestId></ResponseMetadata>
            </SendGreetingResponse>
            """
        );

        var output = await protocol.DeserializeResponseAsync(response);

        Assert.Equal("Hello", output.Greeting);
    }

    [Fact]
    public async Task Ec2QueryDeserializesTheResponseRootWithoutAResultWrapper()
    {
        var protocol = Bind<Ec2QueryProtocol>(Operation("SendGreeting"));
        var response = Response(
            HttpStatusCode.OK,
            """
            <SendGreetingResponse xmlns="https://example.com/">
              <Greeting>Hello</Greeting><requestId>abc</requestId>
            </SendGreetingResponse>
            """
        );

        var output = await protocol.DeserializeResponseAsync(response);

        Assert.Equal("Hello", output.Greeting);
    }

    [Fact]
    public async Task QueryProtocolsDeserializeTheirDistinctErrorEnvelopes()
    {
        var operation = Operation("SendGreeting", withError: true);
        var awsProtocol = Bind<AwsQueryProtocol>(operation);
        var ec2Protocol = Bind<Ec2QueryProtocol>(operation);

        var awsError = await awsProtocol.DeserializeErrorAsync(
            Response(
                HttpStatusCode.BadRequest,
                "<ErrorResponse><Error><Code>GreetingError</Code><Message>bad</Message><Detail>aws</Detail></Error></ErrorResponse>"
            )
        );
        var ec2Error = await ec2Protocol.DeserializeErrorAsync(
            Response(
                HttpStatusCode.BadRequest,
                "<Response><Errors><Error><Code>GreetingError</Code><Message>bad</Message><Detail>ec2</Detail></Error></Errors></Response>"
            )
        );

        Assert.Equal("aws", Assert.IsType<GreetingException>(awsError).Detail);
        Assert.Equal("ec2", Assert.IsType<GreetingException>(ec2Error).Detail);
    }

    private static IClientOperationProtocol<QueryInput, GreetingOutput> Bind<TProtocol>(
        OperationSchema<QueryInput, GreetingOutput> operation
    )
        where TProtocol : IProtocol, new() =>
        new TProtocol()
            .ForService(Schemas.Service(new ShapeId("example", "Service"), "2020-01-08"))
            .ForClientOperation(operation);

    private static OperationSchema<QueryInput, GreetingOutput> Operation(
        string name,
        bool withError = false
    ) =>
        Schemas.Operation(
            new ShapeId("example", name),
            InputSchema(),
            OutputSchema(),
            withError
                ?
                [
                    Schemas.OperationError(
                        new ShapeId("example", "GreetingError"),
                        ErrorSchema(),
                        400
                    ),
                ]
                : []
        );

    private static StructSchema<QueryInput, QueryInputBuilder> InputSchema()
    {
        var stringList = Schemas.List(new ShapeId("example", "StringList"), Schemas.String);
        var stringMap = Schemas.Map(new ShapeId("example", "StringMap"), Schemas.String);
        return Schemas
            .Structure<QueryInput, QueryInputBuilder>(new ShapeId("example", "QueryInput"))
            .Optional(
                "Text",
                static value => value.Text,
                static (builder, value) => builder.Text = value,
                Schemas.NullableReference(Schemas.String)
            )
            .Optional(
                "When",
                static value => value.When,
                static (builder, value) => builder.When = value,
                Schemas.Nullable(Schemas.Timestamp)
            )
            .Optional(
                "Items",
                static value => value.Items,
                static (builder, value) => builder.Items = value,
                Schemas.NullableReference(stringList)
            )
            .Optional(
                "FlatItems",
                static value => value.FlatItems,
                static (builder, value) => builder.FlatItems = value,
                Schemas.NullableReference(stringList),
                [XmlFlattened]
            )
            .Optional(
                "Tags",
                static value => value.Tags,
                static (builder, value) => builder.Tags = value,
                Schemas.NullableReference(stringMap)
            )
            .Optional(
                "Nested",
                static value => value.Nested,
                static (builder, value) => builder.Nested = value,
                Schemas.NullableReference(NestedSchema())
            )
            .Build(
                static () => new QueryInputBuilder(),
                static builder => new QueryInput(
                    builder.Text,
                    builder.When,
                    builder.Items,
                    builder.FlatItems,
                    builder.Tags,
                    builder.Nested
                )
            );
    }

    private static StructSchema<Nested, NestedBuilder> NestedSchema() =>
        Schemas
            .Structure<Nested, NestedBuilder>(new ShapeId("example", "Nested"))
            .Optional(
                "Value",
                static value => value.Value,
                static (builder, value) => builder.Value = value,
                Schemas.NullableReference(Schemas.String)
            )
            .Build(static () => new NestedBuilder(), static builder => new Nested(builder.Value));

    private static StructSchema<GreetingOutput, GreetingOutputBuilder> OutputSchema() =>
        Schemas
            .Structure<GreetingOutput, GreetingOutputBuilder>(
                new ShapeId("example", "GreetingOutput")
            )
            .Optional(
                "Greeting",
                static value => value.Greeting,
                static (builder, value) => builder.Greeting = value,
                Schemas.NullableReference(Schemas.String)
            )
            .Build(
                static () => new GreetingOutputBuilder(),
                static builder => new GreetingOutput(builder.Greeting)
            );

    private static StructSchema<GreetingException, GreetingErrorBuilder> ErrorSchema() =>
        Schemas
            .Structure<GreetingException, GreetingErrorBuilder>(
                new ShapeId("example", "GreetingError")
            )
            .Optional(
                "Message",
                static value => value.Message,
                static (builder, value) => builder.Message = value,
                Schemas.NullableReference(Schemas.String)
            )
            .Optional(
                "Detail",
                static value => value.Detail,
                static (builder, value) => builder.Detail = value,
                Schemas.NullableReference(Schemas.String)
            )
            .Build(
                static () => new GreetingErrorBuilder(),
                static builder => new GreetingException(builder.Message) { Detail = builder.Detail }
            );

    private static string BodyText(SmithyHttpRequest request) =>
        Encoding.UTF8.GetString(Assert.IsType<SmithyHttpBody.Bytes>(request.Body).Content);

    private static SmithyHttpClientResponse Response(HttpStatusCode code, string body) =>
        new(
            code,
            null,
            Encoding.UTF8.GetBytes(body),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        );
}

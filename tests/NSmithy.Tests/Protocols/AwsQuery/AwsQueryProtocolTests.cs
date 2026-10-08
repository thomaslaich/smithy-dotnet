using System.Net;
using System.Text;
using NSmithy.Http;
using NSmithy.Protocols.AwsQuery;
using Nsmithy.Tests.Awsquery;

namespace NSmithy.Tests.Protocols.AwsQuery;

public sealed class AwsQueryProtocolTests
{
    [Fact]
    public void AwsQuerySerializesOfficialFormLayout()
    {
        var protocol = Bind<AwsQueryProtocol>();
        var input = new SendGreetingInput(
            Text: "hello world",
            When: DateTimeOffset.Parse("2015-01-25T08:00:00Z"),
            Items: new StringList(["a", "b"]),
            FlatItems: new StringList(["c", "d"]),
            Tags: new StringMap(
                new Dictionary<string, string> { ["first"] = "1", ["second"] = "2" }
            ),
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
        var protocol = Bind<Ec2QueryProtocol>();
        var input = new SendGreetingInput(
            Text: "hello",
            Items: new StringList(["a", "b"]),
            Nested: new Nested("v")
        );

        var request = protocol.SerializeRequest(input);

        Assert.Equal(
            "Action=SendGreeting&Version=2020-01-08&Text=hello&Items.1=a&Items.2=b&Nested.Value=v",
            BodyText(request)
        );
    }

    [Fact]
    public async Task AwsQueryDeserializesTheResultWrapper()
    {
        var protocol = Bind<AwsQueryProtocol>();
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
        var protocol = Bind<Ec2QueryProtocol>();
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
        var awsProtocol = Bind<AwsQueryProtocol>();
        var ec2Protocol = Bind<Ec2QueryProtocol>();

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

        Assert.Equal("aws", Assert.IsType<GreetingError>(awsError).Detail);
        Assert.Equal("ec2", Assert.IsType<GreetingError>(ec2Error).Detail);
    }

    private static IClientOperationProtocol<SendGreetingInput, SendGreetingOutput> Bind<TProtocol>()
        where TProtocol : IProtocol, new() =>
        new TProtocol()
            .ForService(FixturesSchema.Schema)
            .ForClientOperation(SendGreetingSchema.Schema);

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

using System.Net;
using Example.Hello;
using NSmithy.Codecs.Xml;

var name = args.Length > 0 ? args[0] : "world";
var httpClient = new HttpClient(new MockRestXmlHandler())
{
    BaseAddress = new Uri("https://example.test"),
};

var xmlClient = new HelloXmlServiceClient(httpClient);

var xmlHello = await xmlClient.SayHelloXmlAsync(new SayHelloXmlInput(name));
Console.WriteLine($"SayHelloXml => {xmlHello.Message} from {xmlHello.From}");

internal sealed class MockRestXmlHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return request.RequestUri?.PathAndQuery switch
        {
            "/xml/hello" => HandleRestXmlAsync(request, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unexpected request URI '{request.RequestUri?.PathAndQuery}'."
            ),
        };
    }

    private static Task<HttpResponseMessage> HandleRestXmlAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        ValidateRestXmlRequest(request);
        var body =
            request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult() ?? [];
        var input = XmlCodec.FromSchema(SayHelloXmlInputSchema.Schema).Deserialize(body);
        var output = new SayHelloXmlOutput("mock-restxml", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateXmlResponse(
                HttpStatusCode.OK,
                XmlCodec.FromSchema(SayHelloXmlOutputSchema.Schema).Serialize(output)
            )
        );
    }

    private static void ValidateRestXmlRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
            throw new InvalidOperationException("Expected POST.");

        var contentType = request.Content?.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Unexpected Content-Type '{contentType ?? "<missing>"}'."
            );
    }

    private static HttpResponseMessage CreateXmlResponse(HttpStatusCode statusCode, byte[] body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/xml"
        );
        return response;
    }
}

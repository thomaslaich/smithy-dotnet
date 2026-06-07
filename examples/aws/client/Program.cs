using System.Net;
using Example.Hello;
using NSmithy.Client;
using NSmithy.Codecs.Cbor;
using NSmithy.Codecs.Xml;
using NSmithy.Core;
using NSmithy.Core.Serde;

var name = args.Length > 0 ? args[0] : "world";
var httpClient = new HttpClient(new MockAwsProtocolsHandler());

var client = new HelloServiceClient(
    httpClient,
    new SmithyClientOptions { Endpoint = new Uri("https://example.test") }
);

var xmlClient = new HelloXmlServiceClient(
    httpClient,
    new SmithyClientOptions { Endpoint = new Uri("https://example.test") }
);

try
{
    var hello = await client.SayHelloAsync(new SayHelloInput(name));
    Console.WriteLine($"SayHello => {hello.Message} from {hello.From}");
}
catch (InvalidName error)
{
    Console.WriteLine($"InvalidName => {error.Message}");
}

var xmlHello = await xmlClient.SayHelloXmlAsync(new SayHelloXmlInput(name));
Console.WriteLine($"SayHelloXml => {xmlHello.Message} from {xmlHello.From}");

internal sealed class MockAwsProtocolsHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        return request.RequestUri?.PathAndQuery switch
        {
            "/service/HelloService/operation/SayHello" => HandleRpcV2CborAsync(
                request,
                cancellationToken
            ),
            "/xml/hello" => HandleRestXmlAsync(request, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unexpected request URI '{request.RequestUri?.PathAndQuery}'."
            ),
        };
    }

    private static Task<HttpResponseMessage> HandleRpcV2CborAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        ValidateRpcV2CborRequest(request);
        var body =
            request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult() ?? [];
        var input = FunctionalCborCodec
            .FromSchema(SayHelloInputSchema.FunctionalSchema)
            .Deserialize(body);

        if (string.Equals(input.Name, "error", StringComparison.OrdinalIgnoreCase))
        {
            var error = new RpcV2ErrorEnvelope(
                "example.hello#InvalidName",
                "name must not be 'error'"
            );

            return Task.FromResult(
                CreateResponse(
                    HttpStatusCode.BadRequest,
                    FunctionalCborCodec.FromSchema(RpcV2ErrorEnvelope.Schema).Serialize(error)
                )
            );
        }

        var output = new SayHelloOutput("mock-rpcv2cbor", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateResponse(
                HttpStatusCode.OK,
                FunctionalCborCodec.FromSchema(SayHelloOutputSchema.FunctionalSchema).Serialize(output)
            )
        );
    }

    private static Task<HttpResponseMessage> HandleRestXmlAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        ValidateRestXmlRequest(request);
        var body =
            request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult() ?? [];
        var input = FunctionalXmlCodec
            .FromSchema(SayHelloXmlInputSchema.FunctionalSchema)
            .Deserialize(body);
        var output = new SayHelloXmlOutput("mock-restxml", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateXmlResponse(
                HttpStatusCode.OK,
                FunctionalXmlCodec.FromSchema(SayHelloXmlOutputSchema.FunctionalSchema).Serialize(output)
            )
        );
    }

    private static void ValidateRpcV2CborRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("Expected POST.");
        }

        if (!request.Headers.TryGetValues("Smithy-Protocol", out var protocolValues))
        {
            throw new InvalidOperationException("Missing Smithy-Protocol header.");
        }

        if (!protocolValues.Contains("rpc-v2-cbor", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Unexpected Smithy-Protocol header.");
        }
    }

    private static void ValidateRestXmlRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("Expected POST.");
        }

        var contentType = request.Content?.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unexpected Content-Type '{contentType ?? "<missing>"}'."
            );
        }
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, byte[] body)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(body) };
        response.Headers.Add("Smithy-Protocol", "rpc-v2-cbor");
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/cbor"
        );
        return response;
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

/// <summary>
/// A minimal schema-driven error envelope for rpcv2Cbor error responses.
/// Used by the mock handler to serialize error payloads.
/// </summary>
internal sealed record class RpcV2ErrorEnvelope(string Type, string? Message)
{
    public sealed class Builder
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
    }

    public static FunctionalSchema<RpcV2ErrorEnvelope> Schema { get; } =
        FunctionalSchemas
            .Structure<RpcV2ErrorEnvelope, Builder>(
                ShapeId.Parse("example.transport#RpcV2ErrorEnvelope")
            )
            .Required(
                "__type",
                static value => value.Type,
                static (builder, value) => builder.Type = value,
                FunctionalSchemas.String
            )
            .Optional(
                "message",
                static value => value.Message,
                static (builder, value) => builder.Message = value,
                FunctionalSchemas.String
            )
            .Build(
                static () => new Builder(),
                static builder => new RpcV2ErrorEnvelope(
                    builder.Type
                        ?? throw new InvalidOperationException(
                            "Missing required member '__type'."
                        ),
                    builder.Message
                )
            );
}

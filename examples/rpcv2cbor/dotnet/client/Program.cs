using System.Net;
using Example.Hello;
using SmithyNet.Client;
using SmithyNet.Core;
using SmithyNet.Core.Annotations;

var name = args.Length > 0 ? args[0] : "world";

var client = new HelloServiceClient(
    new HttpClient(new MockRpcV2CborHandler()),
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

internal sealed class MockRpcV2CborHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        ValidateRequest(request);

        var body = request.Content?.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult()
            ?? [];
        var input = SmithyCborPayloadCodec.Default.Deserialize<SayHelloInput>(body);

        if (string.Equals(input.Name, "error", StringComparison.OrdinalIgnoreCase))
        {
            var error = new RpcV2ErrorEnvelope(
                "example.hello#InvalidName",
                "name must not be 'error'"
            );

            return Task.FromResult(
                CreateResponse(
                    HttpStatusCode.BadRequest,
                    SmithyCborPayloadCodec.Default.Serialize(error)
                )
            );
        }

        var output = new SayHelloOutput("mock-rpcv2cbor", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateResponse(
                HttpStatusCode.OK,
                SmithyCborPayloadCodec.Default.Serialize(output)
            )
        );
    }

    private static void ValidateRequest(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("Expected POST.");
        }

        if (request.RequestUri?.PathAndQuery != "/service/HelloService/operation/SayHello")
        {
            throw new InvalidOperationException(
                $"Unexpected request URI '{request.RequestUri?.PathAndQuery}'."
            );
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

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, byte[] body)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(body)
        };
        response.Headers.Add("Smithy-Protocol", "rpc-v2-cbor");
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/cbor");
        return response;
    }
}

[SmithyShape("example.transport#RpcV2ErrorEnvelope", ShapeKind.Structure)]
internal sealed record class RpcV2ErrorEnvelope(
    [property: SmithyMember("__type", "smithy.api#String", IsRequired = true)] string Type,
    [property: SmithyMember("message", "smithy.api#String")] string? Message
);

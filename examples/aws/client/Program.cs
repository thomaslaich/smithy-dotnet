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
        var input = SmithyCborCodec.Default.Deserialize<SayHelloInput>(body);

        if (string.Equals(input.Name, "error", StringComparison.OrdinalIgnoreCase))
        {
            var error = new RpcV2ErrorEnvelope(
                "example.hello#InvalidName",
                "name must not be 'error'"
            );

            return Task.FromResult(
                CreateResponse(HttpStatusCode.BadRequest, SmithyCborCodec.Default.Serialize(error))
            );
        }

        var output = new SayHelloOutput("mock-rpcv2cbor", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateResponse(HttpStatusCode.OK, SmithyCborCodec.Default.Serialize(output))
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
        var input = SmithyXmlCodec.Default.Deserialize<SayHelloXmlInput>(body);
        var output = new SayHelloXmlOutput("mock-restxml", $"Hello, {input.Name}!");
        return Task.FromResult(
            CreateXmlResponse(HttpStatusCode.OK, SmithyXmlCodec.Default.Serialize(output))
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
    : ISerializableStruct, IDeserializableShape<RpcV2ErrorEnvelope>
{
    private static readonly Schema TypeSchema = Schema.CreateMember(
        ShapeId.Parse("example.transport#RpcV2ErrorEnvelope$__type"),
        () => PreludeSchemas.String
    );

    private static readonly Schema MessageSchema = Schema.CreateMember(
        ShapeId.Parse("example.transport#RpcV2ErrorEnvelope$message"),
        () => PreludeSchemas.String
    );

    public static Schema Schema { get; } = Schema.CreateStructure(
        ShapeId.Parse("example.transport#RpcV2ErrorEnvelope"),
        [TypeSchema, MessageSchema]
    );

    Schema ISerializableShape.Schema => Schema;

    public void Serialize(IShapeSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        serializer.WriteStruct(Schema, this);
    }

    public void SerializeMembers(IShapeSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        serializer.WriteString(TypeSchema, Type);
        if (Message is { } msg)
        {
            serializer.WriteString(MessageSchema, msg);
        }
    }

    public static RpcV2ErrorEnvelope Deserialize(IShapeDeserializer deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        string? type = null;
        string? message = null;
        deserializer.ReadStruct<object?>(
            Schema,
            null,
            new StructMemberConsumer<object?>(Member: (_, member, reader) =>
            {
                if (member.MemberName == "__type")
                    type = reader.ReadString(member);
                else if (member.MemberName == "message")
                    message = reader.ReadString(member);
            })
        );
        return new RpcV2ErrorEnvelope(
            type ?? throw new InvalidOperationException("Missing required member '__type'."),
            message
        );
    }
}

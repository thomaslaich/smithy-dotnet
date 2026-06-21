using System.Buffers.Binary;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Grpc;

namespace NSmithy.Tests.Protocols.Grpc;

/// <summary>
/// Exercises the native gRPC protocol end to end through the shared
/// <see cref="IServiceProtocol"/>/<see cref="IOperationProtocol{TInput, TOutput}"/> contract: client
/// serialize → server deserialize → server serialize → client deserialize, plus the framing and
/// error paths. No protoc, Grpc.Tools, or Grpc.Net is involved.
/// </summary>
public sealed class GrpcProtocolTests
{
    private static readonly ShapeId ProtoIndex = ShapeId.Parse("alloy.proto#protoIndex");

    private static IEnumerable<Trait> Field(int index) =>
        [new Trait(ProtoIndex, Document.From(index))];

    public sealed record Echo(string Message);

    public sealed class EchoBuilder
    {
        public string? Message { get; set; }
    }

    private static Schema<Echo> EchoSchema(string name) =>
        Schemas
            .Structure<Echo, EchoBuilder>(ShapeId.Parse($"example.greeter#{name}"))
            .Required("message", x => x.Message, (b, v) => b.Message = v, Schemas.String, Field(1))
            .Build(() => new EchoBuilder(), b => new Echo(b.Message!));

    private static IOperationProtocol<Echo, Echo> BuildProtocol()
    {
        var service = Schemas.Service(ShapeId.Parse("example.greeter#Greeter"));
        var operation = Schemas.Operation(
            ShapeId.Parse("example.greeter#SayHello"),
            EchoSchema("SayHelloInput"),
            EchoSchema("SayHelloOutput")
        );
        return new GrpcProtocol().ForService(service).ForOperation(operation);
    }

    [Fact]
    public void FramesAndUnframesAMessage()
    {
        byte[] payload = [0x0A, 0x02, 0x68, 0x69];

        var frame = GrpcMessageFraming.Frame(payload);

        Assert.Equal(0, frame[0]); // uncompressed
        Assert.Equal(
            (uint)payload.Length,
            BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1, 4))
        );
        Assert.Equal(payload, GrpcMessageFraming.ReadSingle(frame));
    }

    [Fact]
    public void SerializesRequestToGrpcMethodPath()
    {
        var protocol = BuildProtocol();

        var request = protocol.SerializeRequest(new Echo("hi"));

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/example.greeter.Greeter/SayHello", request.RequestUri);
        Assert.Equal("application/grpc+proto", request.ContentType);
        string[] trailers = ["trailers"];
        Assert.Equal(trailers, request.Headers["te"]);
        // 5-byte frame header + proto body (0A 02 'h' 'i')
        Assert.Equal(GrpcMessageFraming.HeaderLength + 4, request.Content!.Length);
    }

    [Fact]
    public void RoundTripsClientToServerToClient()
    {
        var protocol = BuildProtocol();

        // client side
        var request = protocol.SerializeRequest(new Echo("ping"));

        // server side
        var serverInput = protocol.DeserializeRequest(request);
        Assert.Equal(new Echo("ping"), serverInput);
        var response = protocol.SerializeResponse(new Echo("pong"));
        Assert.False(protocol.IsErrorResponse(response));
        string[] okStatus = ["0"];
        Assert.Equal(okStatus, response.Headers["grpc-status"]);

        // client side
        var clientOutput = protocol.DeserializeResponse(response);
        Assert.Equal(new Echo("pong"), clientOutput);
    }

    [Fact]
    public void SerializesAndDiscriminatesModeledErrors()
    {
        var protocol = BuildProtocol();
        var errorSchema = EchoSchema("ThrottlingError");

        var response = protocol.SerializeError(
            errorSchema,
            new Echo("slow down"),
            "example.greeter#ThrottlingError",
            429
        );

        Assert.True(protocol.IsErrorResponse(response));
        // HTTP 429 → gRPC RESOURCE_EXHAUSTED (8)
        string[] exhaustedStatus = ["8"];
        Assert.Equal(exhaustedStatus, response.Headers["grpc-status"]);
        Assert.Equal("example.greeter#ThrottlingError", protocol.GetErrorDiscriminator(response));
        Assert.Equal(new Echo("slow down"), protocol.DeserializeError(errorSchema, response));
    }
}

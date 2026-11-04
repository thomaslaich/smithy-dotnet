using System.Net;
using System.Text;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.AwsJson;
using NSmithy.Protocols.AwsQuery;
using NSmithy.Protocols.Grpc;
using NSmithy.Protocols.RestJson;
using NSmithy.Protocols.RpcV2Cbor;
using NSmithy.Protocols.RpcV2Json;
using Nsmithy.Tests.Core;
using Nsmithy.Tests.Core.Schemas;
using Nsmithy.Tests.Server;
using Nsmithy.Tests.Server.Schemas;
using GrpcFixtures = Nsmithy.Tests.Grpc;

namespace NSmithy.Tests.Protocols;

/// <summary>
/// An error listed on a service applies to its operations, whose schemas do not list it: every
/// protocol takes it from the service schema it is bound to.
/// </summary>
public sealed class ServiceErrorProtocolTests
{
    private static readonly ServiceSchema WithoutErrors = Schemas.Service(
        ShapeId.Parse("example#Service"),
        "1"
    );

    private static readonly ServiceSchema WithError = Schemas.Service(
        ShapeId.Parse("example#Service"),
        "1",
        errors: [Schemas.OperationError(BadRequestSchema.Schema.Id, BadRequestSchema.Schema, 400)]
    );

    public static TheoryData<IProtocol> ServerProtocols =>
        new()
        {
            new RestJson1Protocol(),
            new SimpleRestJsonProtocol(),
            new RpcV2CborProtocol(),
            new RpcV2JsonProtocol(),
        };

    [Theory]
    [MemberData(nameof(ServerProtocols))]
    public void AServerSerializesAServiceError(IProtocol protocol)
    {
        var exception = new BadRequest("Nope.");

        Assert.False(
            protocol
                .ForService(WithoutErrors)
                .ForServerOperation(CreateUserSchema.Schema)
                .TrySerializeError(exception, out _)
        );
        Assert.True(
            protocol
                .ForService(WithError)
                .ForServerOperation(CreateUserSchema.Schema)
                .TrySerializeError(exception, out _)
        );
    }

    [Fact]
    public void AGrpcServerSerializesAServiceError()
    {
        var schema = GrpcFixtures.Schemas.ThrottlingErrorSchema.Schema;
        var service = Schemas.Service(
            ShapeId.Parse("example#Service"),
            "1",
            errors: [Schemas.OperationError(schema.Id, schema, 429)]
        );
        var exception = new GrpcFixtures.ThrottlingError("Slow down.");

        Assert.False(
            new GrpcProtocol()
                .ForService(WithoutErrors)
                .ForServerOperation(GrpcFixtures.Schemas.WatchConstrainedSchema.Schema)
                .TrySerializeError(exception, out _)
        );
        Assert.True(
            new GrpcProtocol()
                .ForService(service)
                .ForServerOperation(GrpcFixtures.Schemas.WatchConstrainedSchema.Schema)
                .TrySerializeError(exception, out _)
        );
    }

    public static TheoryData<IProtocol, string, string?, string> ClientResponses =>
        new()
        {
            {
                new RestJson1Protocol(),
                "X-Amzn-Errortype",
                "BadRequest",
                """{"message":"Nope."}"""
            },
            {
                new SimpleRestJsonProtocol(),
                "X-Error-Type",
                "BadRequest",
                """{"message":"Nope."}"""
            },
            {
                new RpcV2JsonProtocol(),
                "smithy-protocol",
                "rpc-v2-json",
                """{"__type":"BadRequest","message":"Nope."}"""
            },
            {
                new AwsJson10Protocol(),
                "X-Unused",
                null,
                """{"__type":"BadRequest","message":"Nope."}"""
            },
            {
                new AwsJson11Protocol(),
                "X-Unused",
                null,
                """{"__type":"BadRequest","message":"Nope."}"""
            },
            {
                new AwsQueryProtocol(),
                "X-Unused",
                null,
                "<ErrorResponse><Error><Code>BadRequest</Code><Message>Nope.</Message></Error></ErrorResponse>"
            },
            {
                new Ec2QueryProtocol(),
                "X-Unused",
                null,
                "<Response><Errors><Error><Code>BadRequest</Code><Message>Nope.</Message></Error></Errors></Response>"
            },
        };

    [Theory]
    [MemberData(nameof(ClientResponses))]
    public async Task AClientDeserializesAServiceError(
        IProtocol protocol,
        string header,
        string? headerValue,
        string body
    )
    {
        var error = await protocol
            .ForService(WithError)
            .ForClientOperation(CreateUserSchema.Schema)
            .DeserializeErrorAsync(Response(header, headerValue, body));

        Assert.IsType<BadRequest>(error);
    }

    private static SmithyHttpClientResponse Response(string header, string? value, string body)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        if (value is not null)
        {
            headers[header] = [value];
        }

        return new SmithyHttpClientResponse(
            HttpStatusCode.BadRequest,
            null,
            Encoding.UTF8.GetBytes(body),
            headers,
            headers
        );
    }
}

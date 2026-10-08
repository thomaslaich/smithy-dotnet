using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;
using NSmithy.Server.Mcp;

var inputSchema = new AotInputSchema();
var operation = Schemas.Operation(
    new ShapeId("example", "UpdateUser"),
    inputSchema,
    Schemas.Unit,
    traits: [RestTraits.HttpTrait("PUT", "/users/{userId}")]
);
var service = new RestJson1Protocol().ForService(
    Schemas.Service(new ShapeId("example", "Service"))
);
var clientProtocol = service.ForClientOperation(operation);
var serverProtocol = service.ForServerOperation(operation);
var expected = new AotInput("ada lovelace", "token-123", 25, "Ada");

var request = clientProtocol.SerializeRequest(expected);
var actual = await serverProtocol.DeserializeRequestAsync(request);

if (actual != expected)
{
    throw new InvalidOperationException($"REST/JSON NativeAOT round trip failed: {actual}");
}

var mcpTools = SmithyMcpTools.Create(
    new ServiceOperationCatalog(
        Schemas.Service(new ShapeId("example", "Service")),
        ServiceOperation.Create(
            operation,
            static (AotInput _, CancellationToken _) => Task.FromResult(SmithyUnit.Value),
            new OperationJsonSchemas(
                """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"userId":{"type":"string"},"requestToken":{"type":"string"},"pageSize":{"type":"integer"},"displayName":{"type":"string"}},"required":["userId","pageSize","displayName"],"additionalProperties":false}
                """,
                """
                {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{},"additionalProperties":false}
                """
            )
        )
    )
);
var mcpTool = mcpTools.Single().ProtocolTool;
if (mcpTool.Name != "UpdateUser" || mcpTool.InputSchema.GetProperty("type").GetString() != "object")
{
    throw new InvalidOperationException("MCP NativeAOT tool projection failed.");
}

var mcpPrompt = SmithyMcpPrompts
    .Create([
        new ServicePromptDefinition(
            "update_user",
            "Update one user",
            "Use UpdateUser for {{userId}}.",
            arguments: [new ServicePromptArgumentDefinition("userId", null, true)]
        ),
    ])
    .Single()
    .ProtocolPrompt;
if (mcpPrompt.Name != "update_user" || mcpPrompt.Arguments?.Single().Name != "userId")
{
    throw new InvalidOperationException("MCP NativeAOT prompt projection failed.");
}

internal sealed record AotInput(
    string UserId,
    string? RequestToken,
    int PageSize,
    string DisplayName
);

internal sealed class AotInputBuilder
{
    public string? UserId { get; set; }

    public string? RequestToken { get; set; }

    public int PageSize { get; set; }

    public string? DisplayName { get; set; }
}

internal sealed class AotInputSchema()
    : StructSchema<AotInput, AotInputBuilder>(
        new ShapeId("example", "AotInput"),
        [
            new("userId", Schemas.String, isRequired: true, [RestTraits.HttpLabelTrait]),
            new(
                "requestToken",
                Schemas.String,
                traits: [RestTraits.HttpHeaderTrait("X-Request-Token")]
            ),
            new(
                "pageSize",
                Schemas.Integer,
                isRequired: true,
                [RestTraits.HttpQueryTrait("pageSize")]
            ),
            new("displayName", Schemas.String, isRequired: true),
        ]
    )
{
    private static readonly Schema<string?> OptionalString = Schemas.NullableReference(
        Schemas.String
    );

    public override AotInputBuilder CreateTypedBuilder() => new();

    public override AotInput Build(AotInputBuilder builder) =>
        new(builder.UserId!, builder.RequestToken, builder.PageSize, builder.DisplayName!);

    public override void SerializeMembers<TSerializer>(AotInput value, ref TSerializer serializer)
    {
        Schemas.String.Write(0, value.UserId, ref serializer);
        OptionalString.Write(1, value.RequestToken, ref serializer);
        Schemas.Integer.Write(2, value.PageSize, ref serializer);
        Schemas.String.Write(3, value.DisplayName, ref serializer);
    }

    public override void DeserializeMember<TDeserializer>(
        AotInputBuilder builder,
        int index,
        ref TDeserializer deserializer
    )
    {
        switch (index)
        {
            case 0:
                builder.UserId = Schemas.String.Read(ref deserializer);
                break;
            case 1:
                builder.RequestToken = OptionalString.Read(ref deserializer);
                break;
            case 2:
                builder.PageSize = Schemas.Integer.Read(ref deserializer);
                break;
            case 3:
                builder.DisplayName = Schemas.String.Read(ref deserializer);
                break;
        }
    }
}

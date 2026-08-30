using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;

var inputSchema = Schemas
    .Structure<AotInput, AotInputBuilder>(new ShapeId("example", "AotInput"))
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
        "pageSize",
        static input => input.PageSize,
        static (builder, value) => builder.PageSize = value,
        Schemas.Integer,
        traits: [RestTraits.HttpQueryTrait("pageSize")]
    )
    .Required(
        "displayName",
        static input => input.DisplayName,
        static (builder, value) => builder.DisplayName = value,
        Schemas.String
    )
    .Build(
        static () => new AotInputBuilder(),
        static builder => new AotInput(
            builder.UserId!,
            builder.RequestToken,
            builder.PageSize,
            builder.DisplayName!
        )
    );
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

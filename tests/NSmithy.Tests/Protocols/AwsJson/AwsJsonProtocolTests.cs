using System.Net;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.AwsJson;

namespace NSmithy.Tests.Protocols.AwsJson;

public sealed class AwsJsonProtocolTests
{
    // Shaped like a generated error: the message plus one optional parameter per member.
    public sealed class ThrottledException(
        string? message = null,
        string? reason = null,
        int? retryAfter = null
    ) : Exception(message)
    {
        public string? Reason { get; } = reason;

        public int? RetryAfter { get; } = retryAfter;
    }

    public sealed class ThrottledExceptionBuilder
    {
        public string? Reason { get; set; }

        public int? RetryAfter { get; set; }
    }

    public sealed record Empty;

    private static readonly Schema<Empty> EmptySchema = Schemas
        .Structure<Empty, Empty>(ShapeId.Parse("example#Empty"))
        .Build(static () => new Empty(), static value => value);

    private static readonly Schema<ThrottledException> ThrottledExceptionSchema = Schemas
        .Structure<ThrottledException, ThrottledExceptionBuilder>(
            ShapeId.Parse("example#ThrottledError")
        )
        .Optional(
            "reason",
            static value => value.Reason,
            static (builder, value) => builder.Reason = value,
            Schemas.NullableReference(Schemas.String)
        )
        .Optional(
            "retryAfter",
            static value => value.RetryAfter,
            static (builder, value) => builder.RetryAfter = value,
            Schemas.Nullable(Schemas.Integer)
        )
        .Build(
            static () => new ThrottledExceptionBuilder(),
            static builder => new ThrottledException(null, builder.Reason, builder.RetryAfter)
        );

    [Fact]
    public async Task ResolvesAnErrorWithMembersFromAnEmptyBody()
    {
        var operation = Schemas.Operation(
            ShapeId.Parse("example#Ping"),
            EmptySchema,
            EmptySchema,
            [
                Schemas.OperationError(
                    ShapeId.Parse("example#ThrottledError"),
                    ThrottledExceptionSchema,
                    429
                ),
            ]
        );
        var protocol = new AwsJson10Protocol()
            .ForService(Schemas.Service(ShapeId.Parse("example#Service")))
            .ForClientOperation(operation);
        var response = new SmithyHttpClientResponse(
            (HttpStatusCode)429,
            null,
            [],
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["X-Amzn-Errortype"] = ["ThrottledError"],
            },
            new Dictionary<string, IReadOnlyList<string>>()
        );

        var error = await protocol.DeserializeErrorAsync(response);

        var throttled = Assert.IsType<ThrottledException>(error);
        Assert.Null(throttled.Reason);
        Assert.Null(throttled.RetryAfter);
    }
}

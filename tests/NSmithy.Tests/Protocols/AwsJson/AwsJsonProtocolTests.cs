using System.Net;
using NSmithy.Http;
using NSmithy.Protocols.AwsJson;
using Nsmithy.Tests.Awsjson;

namespace NSmithy.Tests.Protocols.AwsJson;

public sealed class AwsJsonProtocolTests
{
    [Fact]
    public async Task ResolvesAnErrorWithMembersFromAnEmptyBody()
    {
        var protocol = new AwsJson10Protocol()
            .ForService(FixturesSchema.Schema)
            .ForClientOperation(PingSchema.Schema);
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

        var throttled = Assert.IsType<ThrottledError>(error);
        Assert.Null(throttled.Reason);
        Assert.Null(throttled.RetryAfter);
    }
}

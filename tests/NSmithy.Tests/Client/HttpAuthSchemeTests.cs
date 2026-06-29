using System.Net;
using System.Text;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class HttpAuthSchemeTests
{
    private static readonly SmithyAuthSchemeContext Context = new(
        new Uri("http://localhost"),
        Schemas.Service(new ShapeId("example", "Svc"))
    );

    [Fact]
    public async Task BearerSchemeSetsAuthorizationHeader()
    {
        var request = await SignAsync(new HttpBearerAuthScheme("my-token"), "/");

        Assert.Equal(["Bearer my-token"], request.Headers["Authorization"]);
    }

    [Fact]
    public async Task BearerSchemeInterceptorSetsAuthorizationHeader()
    {
        var request = await InterceptAsync(new HttpBearerAuthScheme("my-token"), "/");

        Assert.Equal(["Bearer my-token"], request.Headers["Authorization"]);
    }

    [Fact]
    public async Task BasicSchemeSetsBase64AuthorizationHeader()
    {
        var request = await SignAsync(new HttpBasicAuthScheme("alice", "s3cret"), "/");

        var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:s3cret"));
        Assert.Equal([expected], request.Headers["Authorization"]);
    }

    [Fact]
    public async Task ApiKeyHeaderSchemeSetsNamedHeader()
    {
        var request = await SignAsync(new HttpApiKeyAuthScheme("X-Api-Key", "secret"), "/");

        Assert.Equal(["secret"], request.Headers["X-Api-Key"]);
    }

    [Fact]
    public async Task ApiKeyHeaderSchemeAppliesSchemePrefix()
    {
        var request = await SignAsync(
            new HttpApiKeyAuthScheme("Authorization", "secret", ApiKeyLocation.Header, "ApiKey"),
            "/"
        );

        Assert.Equal(["ApiKey secret"], request.Headers["Authorization"]);
    }

    [Fact]
    public async Task ApiKeyQuerySchemeAppendsQueryParameter()
    {
        var request = await SignAsync(
            new HttpApiKeyAuthScheme("api_key", "se cret", ApiKeyLocation.Query),
            "/list?limit=10"
        );

        Assert.Equal("/list?limit=10&api_key=se%20cret", request.RequestUri);
    }

    [Fact]
    public async Task ApiKeyQueryInterceptorAppendsQueryParameter()
    {
        var request = await InterceptAsync(
            new HttpApiKeyAuthScheme("api_key", "se cret", ApiKeyLocation.Query),
            "/list?limit=10"
        );

        Assert.Equal("/list?limit=10&api_key=se%20cret", request.RequestUri);
    }

    [Fact]
    public void SchemeIdsMatchSmithyTraitShapeIds()
    {
        Assert.Equal("smithy.api#httpBearerAuth", new HttpBearerAuthScheme("t").SchemeId);
        Assert.Equal("smithy.api#httpBasicAuth", new HttpBasicAuthScheme("u", "p").SchemeId);
        Assert.Equal("smithy.api#httpApiKeyAuth", new HttpApiKeyAuthScheme("k", "v").SchemeId);
    }

    [Fact]
    public void ApiKeyQueryRejectsSchemePrefix()
    {
        Assert.Throws<ArgumentException>(() =>
            new HttpApiKeyAuthScheme("k", "v", ApiKeyLocation.Query, "ApiKey")
        );
    }

    private static async Task<SmithyHttpRequest> SignAsync(ISmithyAuthScheme scheme, string uri)
    {
        var middleware = scheme.CreateMiddleware(Context);
        SmithyHttpRequest? captured = null;

        await middleware.InvokeAsync(
            new SmithyOperationRequest("Svc", "Op", new SmithyHttpRequest(HttpMethod.Get, uri)),
            (request, _) =>
            {
                captured = request.Request;
                return Task.FromResult(
                    new SmithyOperationResponse(
                        request.ServiceName,
                        request.OperationName,
                        new SmithyHttpResponse(
                            HttpStatusCode.OK,
                            "OK",
                            [],
                            new Dictionary<string, IReadOnlyList<string>>(),
                            new Dictionary<string, IReadOnlyList<string>>()
                        )
                    )
                );
            }
        );

        return captured!;
    }

    private static async Task<SmithyHttpRequest> InterceptAsync(
        ISmithyAuthScheme scheme,
        string uri
    )
    {
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, "Svc");
        context.Set(SmithyContextKeys.OperationName, "Op");

        return await scheme
            .CreateInterceptor(Context)
            .OnBeforeTransmitAsync(context, new SmithyHttpRequest(HttpMethod.Get, uri))
            .ConfigureAwait(false);
    }
}

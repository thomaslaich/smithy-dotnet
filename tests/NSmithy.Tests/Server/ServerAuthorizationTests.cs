using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;
using NSmithy.Server.AspNetCore;

namespace NSmithy.Tests.Server;

public sealed class ServerAuthorizationTests
{
    private static readonly ShapeId ApiKeyAuth = new("smithy.api", "httpApiKeyAuth");

    private static readonly ServiceSchema Service = Schemas.Service(
        new ShapeId("example.auth", "Auth")
    );

    private static readonly OperationSchema<SmithyUnit, SmithyUnit> Ping = Schemas.Operation(
        new ShapeId("example.auth", "Ping"),
        Schemas.Unit,
        Schemas.Unit,
        traits: [RestTraits.HttpTrait("GET", "/ping")]
    );

    private static readonly OperationSchema<SmithyUnit, SmithyUnit> Secret = Schemas.Operation(
        new ShapeId("example.auth", "Secret"),
        Schemas.Unit,
        Schemas.Unit,
        traits: [RestTraits.HttpTrait("GET", "/secret")]
    );

    private static readonly OperationSchema<SmithyUnit, SmithyUnit> Status = Schemas.Operation(
        new ShapeId("example.auth", "Status"),
        Schemas.Unit,
        Schemas.Unit,
        traits: [RestTraits.HttpTrait("GET", "/status")]
    );

    [Fact]
    public void OperationsNeedNoAuthenticationByDefault()
    {
        var operation = Operation(Ping);

        Assert.Equal([ServerOperation.NoAuth], operation.AuthSchemes);
        Assert.False(operation.RequiresAuthentication);
        Assert.True(Operation(Secret, ApiKeyAuth).RequiresAuthentication);
        Assert.False(Operation(Status, ApiKeyAuth, ServerOperation.NoAuth).RequiresAuthentication);
    }

    [Fact]
    public void OperationsNeedAnAuthScheme()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ServerOperation.Create(
                Ping,
                _ => (_, _) => Task.FromResult(SmithyUnit.Value),
                authSchemes: []
            )
        );

        Assert.Equal("authSchemes", exception.ParamName);
    }

    [Fact]
    public async Task EndpointsCarryTheOperationsTheyServe()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("RestJson1", new RestJson1Protocol()),
            new SmithyServerProtocol("Dispatch", new ServerRoutingTests.HeaderDispatchProtocol())
        );

        var metadata = app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.Select(endpoint =>
                endpoint.Metadata.GetRequiredMetadata<SmithyEndpointMetadata>()
            )
            .ToList();

        Assert.Equal(
            [
                "RestJson1 Ping",
                "RestJson1 Secret",
                "RestJson1 Status",
                "Dispatch Ping Secret Status",
            ],
            metadata.Select(m =>
                string.Join(' ', [m.Protocol, .. m.Operations.Select(o => o.Schema.Id.Name)])
            )
        );
        Assert.All(metadata, m => Assert.Same(Service, m.Service));
    }

    [Fact]
    public async Task ModeledAuthorizationProtectsOperationsThatRequireAuthentication()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("RestJson1", new RestJson1Protocol())
        );
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(client, "/secret"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/secret", "valid"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(client, "/secret", "guest"));
    }

    [Fact]
    public async Task ModeledAuthorizationAllowsAnonymousOperationsDespiteAFallbackPolicy()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("RestJson1", new RestJson1Protocol())
        );
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/ping"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/status"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/status", "valid"));
    }

    [Fact]
    public async Task WithoutModeledAuthorizationTheApplicationDecides()
    {
        await using var app = await StartAsync(
            modeledAuthorization: false,
            new SmithyServerProtocol("RestJson1", new RestJson1Protocol())
        );
        using var client = app.GetTestClient();

        // The fallback policy applies to every operation alike.
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(client, "/ping"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, "/secret", "valid"));
    }

    [Fact]
    public async Task DispatchEndpointsAuthorizeOnceTheOperationIsKnown()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("Dispatch", new ServerRoutingTests.HeaderDispatchProtocol())
        );
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, await DispatchAsync(client, "Ping"));
        Assert.Equal(HttpStatusCode.OK, await DispatchAsync(client, "Status"));
        Assert.Equal(HttpStatusCode.Unauthorized, await DispatchAsync(client, "Secret"));
        Assert.Equal(HttpStatusCode.Forbidden, await DispatchAsync(client, "Secret", "guest"));
        Assert.Equal(HttpStatusCode.OK, await DispatchAsync(client, "Secret", "valid"));
    }

    private static ServerOperation<SmithyUnit, SmithyUnit> Operation(
        OperationSchema<SmithyUnit, SmithyUnit> schema,
        params ShapeId[] authSchemes
    ) =>
        ServerOperation.Create(
            schema,
            _ => (_, _) => Task.FromResult(SmithyUnit.Value),
            authSchemes: authSchemes.Length == 0 ? null : authSchemes
        );

    private static Task<WebApplication> StartAsync(params SmithyServerProtocol[] protocols) =>
        StartAsync(modeledAuthorization: true, protocols);

    private static async Task<WebApplication> StartAsync(
        bool modeledAuthorization,
        params SmithyServerProtocol[] protocols
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder
            .Services.AddAuthentication(KeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, KeyAuthenticationHandler>(
                KeyAuthenticationHandler.SchemeName,
                null
            );
        builder.Services.AddAuthorization(options =>
        {
            var members = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireRole("member")
                .Build();
            options.DefaultPolicy = members;
            options.FallbackPolicy = members;
        });
        var app = builder.Build();
        try
        {
            app.UseAuthentication();
            app.UseAuthorization();
            var service = app.MapSmithyService(
                new ServiceOperationCatalog(
                    Service,
                    Operation(Ping),
                    Operation(Secret, ApiKeyAuth),
                    Operation(Status, ApiKeyAuth, ServerOperation.NoAuth)
                ),
                protocols
            );
            if (modeledAuthorization)
            {
                service.WithModeledAuthorization();
            }

            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<HttpStatusCode> GetAsync(
        HttpClient client,
        string path,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (key is not null)
        {
            request.Headers.Add("X-Key", key);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> DispatchAsync(
        HttpClient client,
        string operation,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/rpc");
        request.Headers.Add("X-Operation", operation);
        if (key is not null)
        {
            request.Headers.Add("X-Key", key);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    // Authenticates "valid" as a member and "guest" as a user without the member role.
    private sealed class KeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Key";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Key", out var key))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims = key == "valid" ? [new(ClaimTypes.Role, "member")] : [];
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)
                )
            );
        }
    }
}

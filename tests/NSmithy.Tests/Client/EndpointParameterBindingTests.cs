using NSmithy.Client;
using NSmithy.Core;
using Nsmithy.Tests.Endpointbindings;

namespace NSmithy.Tests.Client;

public sealed class EndpointParameterBindingTests
{
    [Theory]
    [InlineData("Region")]
    [InlineData("Flag")]
    [InlineData("Names")]
    public void RequiredParametersAreCheckedBeforeEvaluatingRules(string missing)
    {
        var parameters = ValidParameters();
        parameters = missing switch
        {
            "Region" => parameters with { Region = null },
            "Flag" => parameters with { Flag = null },
            _ => parameters with { Names = null },
        };

        var error = Assert.Throws<SmithyEndpointResolutionException>(() =>
            RoutingEndpointResolver.Resolve(parameters)
        );

        Assert.Equal($"Endpoint parameter '{missing}' is required.", error.Message);
    }

    [Fact]
    public void FalseAndEmptyListsSatisfyRequiredParameters()
    {
        Assert.Equal(
            new Uri("https://example.com/region/default"),
            RoutingEndpointResolver.Resolve(ValidParameters()).Uri
        );
    }

    [Fact]
    public void ExplicitNullUsesTheModeledDefault()
    {
        Assert.Equal(
            new Uri("https://example.com/region/default"),
            RoutingEndpointResolver.Resolve(ValidParameters() with { Route = null! }).Uri
        );
    }

    [Theory]
    [InlineData("member", "operation", "client", "member")]
    [InlineData(null, "operation", "client", "operation")]
    [InlineData(null, null, "client", "client")]
    [InlineData(null, null, null, "default")]
    public async Task NullableSourcesFallBackInPrecedenceOrder(
        string? member,
        string? operation,
        string? client,
        string expected
    )
    {
        var parameters = ValidParameters();
        if (client is not null)
        {
            parameters = parameters with { Route = client };
        }
        var resolver = new RoutingEndpointResolver(parameters);
        var endpoint = await resolver.ResolveEndpointAsync(
            Parameters(
                "SelectRoute",
                new SelectRouteInput(
                    Member: member,
                    Nested: operation is null ? null : new RouteDetails(operation)
                )
            )
        );

        Assert.Equal(new Uri($"https://example.com/region/{expected}"), endpoint.Uri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaticBindingsWinEvenWithoutAnInputObject(bool hasInput)
    {
        var resolver = new RoutingEndpointResolver(ValidParameters() with { Route = "client" });
        var endpoint = await resolver.ResolveEndpointAsync(
            Parameters(
                "StaticRoute",
                hasInput
                    ? new StaticRouteInput(Member: "member", Nested: new RouteDetails("operation"))
                    : null
            )
        );

        Assert.Equal(new Uri("https://example.com/region/static"), endpoint.Uri);
    }

    [Fact]
    public async Task ConfiguredEndpointBypassesRulesWithoutSdkEndpointParameter()
    {
        var resolver = RoutingEndpointResolver.Resolve(ValidParameters());
        Assert.Equal(new Uri("https://example.com/region/default"), resolver.Uri);
        using var http = new HttpClient(new EmptyHandler());
        using var client = new RoutingClient(
            http,
            new RoutingClientConfig
            {
                Endpoint = new Uri("https://override.example.test"),
                // Region and Flag are deliberately missing: the rules must not run.
            }
        );
        await client.SelectRouteAsync(new SelectRouteInput());
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Assert.Equal("https://override.example.test/select", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                }
            );
        }
    }

    private static RoutingEndpointParameters ValidParameters() =>
        new()
        {
            Region = "region",
            Flag = false,
            Names = [],
        };

    private static SmithyEndpointParameters Parameters(string operation, object? input) =>
        new(
            ShapeId.Parse("nsmithy.tests.endpointbindings#Routing"),
            ShapeId.Parse($"nsmithy.tests.endpointbindings#{operation}"),
            null,
            input
        );
}

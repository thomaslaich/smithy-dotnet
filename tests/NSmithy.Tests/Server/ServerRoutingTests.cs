using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;
using NSmithy.Protocols.Grpc;
using NSmithy.Protocols.Rest;
using NSmithy.Protocols.RestJson;
using NSmithy.Protocols.RpcV2Cbor;
using NSmithy.Server;
using NSmithy.Server.AspNetCore;

namespace NSmithy.Tests.Server;

public sealed class ServerRoutingTests
{
    private static readonly ServiceSchema Service = Schemas.Service(
        new ShapeId("example.routing", "Routing")
    );

    private static readonly OperationSchema<SmithyUnit, SmithyUnit> Ping = Schemas.Operation(
        new ShapeId("example.routing", "Ping"),
        Schemas.Unit,
        Schemas.Unit,
        traits: [RestTraits.HttpTrait("GET", "/ping?mode=fast&verbose")]
    );

    [Fact]
    public void RestRoutesComeFromTheHttpTrait()
    {
        var files = Schemas.Operation(
            new ShapeId("example.routing", "GetFile"),
            Schemas.Unit,
            Schemas.Unit,
            traits: [RestTraits.HttpTrait("GET", "/files/{key+}?download=true")]
        );

        var routing = Assert.IsType<ServerRouting.ByRoute>(
            new RestJson1Protocol().ForService(Service).GetServerRouting([files, Ping])
        );

        Assert.Equal(
            [
                new ServerRoute(files.Id, "GET", "/files/{key+}", [new("download", "true")]),
                new ServerRoute(
                    Ping.Id,
                    "GET",
                    "/ping",
                    [new("mode", "fast"), new("verbose", null)]
                ),
            ],
            routing.Routes,
            RouteComparer.Instance
        );
    }

    [Fact]
    public void RpcV2AndGrpcRoutesComeFromTheShapeIds()
    {
        var rpcv2 = Assert.IsType<ServerRouting.ByRoute>(
            new RpcV2CborProtocol().ForService(Service).GetServerRouting([Ping])
        );
        var grpc = Assert.IsType<ServerRouting.ByRoute>(
            new GrpcProtocol().ForService(Service).GetServerRouting([Ping])
        );

        Assert.Equal("/service/Routing/operation/Ping", Assert.Single(rpcv2.Routes).Path);
        Assert.Equal("/example.routing.Routing/Ping", Assert.Single(grpc.Routes).Path);
    }

    [Fact]
    public async Task RequestsWithoutTheRequiredQueryAreNotRouted()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("RestJson1", new RestJson1Protocol())
        );
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ping?verbose")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/ping?mode=fast&verbose")).StatusCode
        );
    }

    [Fact]
    public async Task ProtocolsWithTheSameRoutesConflict()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StartAsync(
                new SmithyServerProtocol("RestJson1", new RestJson1Protocol()),
                new SmithyServerProtocol("SimpleRestJson", new SimpleRestJsonProtocol())
            )
        );

        Assert.Contains("SimpleRestJson", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'GET /ping'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatchProtocolsResolveTheOperationFromTheRequest()
    {
        await using var app = await StartAsync(
            new SmithyServerProtocol("Dispatch", new HeaderDispatchProtocol())
        );
        using var client = app.GetTestClient();

        using var known = new HttpRequestMessage(HttpMethod.Post, "/rpc");
        known.Headers.Add("X-Operation", "Ping");
        using var unknown = new HttpRequestMessage(HttpMethod.Post, "/rpc");
        unknown.Headers.Add("X-Operation", "Pong");

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(known)).StatusCode);
        Assert.Equal(HttpStatusCode.NotImplemented, (await client.SendAsync(unknown)).StatusCode);
    }

    private static async Task<WebApplication> StartAsync(params SmithyServerProtocol[] protocols)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // No AddSmithyServer: mapped services fall back to a default runtime.
        var app = builder.Build();
        try
        {
            app.MapSmithyService(
                Service,
                [ServerOperation.Create(Ping, _ => (_, _) => Task.FromResult(SmithyUnit.Value))],
                protocols
            );
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    // Names the operation in a header, like AWS JSON's X-Amz-Target, and serializes with
    // restJson1.
    private sealed class HeaderDispatchProtocol : IProtocol
    {
        public IServiceProtocol ForService(ServiceSchema service) =>
            new ServiceProtocol(new RestJson1Protocol().ForService(service), service);

        private sealed class ServiceProtocol(IServiceProtocol inner, ServiceSchema service)
            : IServiceProtocol
        {
            public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
                OperationSchema<TInput, TOutput> operation
            ) => inner.ForClientOperation(operation);

            public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
                OperationSchema<TInput, TOutput> operation
            ) => inner.ForServerOperation(operation);

            public ServerRouting GetServerRouting(IReadOnlyList<IOperationSchema> operations) =>
                new ServerRouting.ByDispatch(
                    "POST",
                    "/rpc",
                    (request, _) =>
                        ValueTask.FromResult<ShapeId?>(
                            request.Headers.TryGetValue("X-Operation", out var names)
                                ? new ShapeId(service.Id.Namespace, names[0])
                                : null
                        ),
                    _ => new SmithyHttpServerResponse { StatusCode = 501 }
                );
        }
    }

    private sealed class RouteComparer : IEqualityComparer<ServerRoute>
    {
        public static RouteComparer Instance { get; } = new();

        public bool Equals(ServerRoute? x, ServerRoute? y) =>
            x is not null
            && y is not null
            && x.Operation == y.Operation
            && x.Method == y.Method
            && x.Path == y.Path
            && x.RequiredQuery.SequenceEqual(y.RequiredQuery);

        public int GetHashCode(ServerRoute obj) => HashCode.Combine(obj.Operation, obj.Path);
    }
}

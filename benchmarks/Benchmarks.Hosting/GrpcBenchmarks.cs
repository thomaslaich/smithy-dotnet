using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Bench.Stacks.GrpcNet;
using Bench.Stacks.NSmithyGrpc;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nsmithy.Bench.Grpc;

namespace Bench.Hosting;

public sealed record GrpcBenchScenario(
    string Name,
    string MethodPath,
    byte[] RequestBody,
    byte[] ResponseBody
)
{
    public HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MethodPath)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(RequestBody),
        };
        request.Headers.TryAddWithoutValidation("te", "trailers");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/grpc+proto");
        return request;
    }
}

public static class GrpcBenchScenarios
{
    public static IReadOnlyList<GrpcBenchScenario> All { get; } =
    [
        new(
            "get-item",
            "/nsmithy.bench.grpc.GrpcBenchmarkService/GetItem",
            Frame(new Bench.GrpcNet.GetItemInput { Id = "item-0" }),
            Frame(new Bench.GrpcNet.GetItemOutput { Item = CreateGrpcNetItem("item-0", 0) })
        ),
        new(
            "list-items-100",
            "/nsmithy.bench.grpc.GrpcBenchmarkService/ListItems",
            Frame(new Bench.GrpcNet.ListItemsInput { Count = 100 }),
            Frame(CreateGrpcNetList(100))
        ),
    ];

    public static GrpcBenchScenario ByName(string name) =>
        All.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown gRPC scenario.");

    private static Bench.GrpcNet.ListItemsOutput CreateGrpcNetList(int count)
    {
        var output = new Bench.GrpcNet.ListItemsOutput();
        output.Items.Add(
            Enumerable.Range(0, count).Select(index => CreateGrpcNetItem($"item-{index}", index))
        );
        return output;
    }

    private static Bench.GrpcNet.Item CreateGrpcNetItem(string id, int index)
    {
        var item = new Bench.GrpcNet.Item
        {
            Id = id,
            Name = $"Benchmark item {index}",
            PriceCents = 1_000 + index,
            InStock = true,
        };
        item.Tags.Add(["benchmark", "grpc", $"tag-{index % 5}"]);
        return item;
    }

    private static byte[] Frame(IMessage message)
    {
        var payload = message.ToByteArray();
        var frame = new byte[payload.Length + 5];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1, 4), payload.Length);
        payload.CopyTo(frame, 5);
        return frame;
    }
}

public static class GrpcBenchStacks
{
    public const string NSmithy = "nsmithy-grpc";
    public const string GrpcNet = "grpc-net";

    public static IReadOnlyList<string> Names { get; } = [NSmithy, GrpcNet];

    public static GrpcBenchServer Start(string name) =>
        name switch
        {
            NSmithy => GrpcBenchServer.Start(
                NSmithy,
                services => services.AddGrpcBenchmarkServiceHandler<NSmithyGrpcBenchmarkHandler>(),
                endpoints => endpoints.MapGrpcBenchmarkService()
            ),
            GrpcNet => GrpcBenchServer.Start(
                GrpcNet,
                services => services.AddGrpc(),
                endpoints => endpoints.MapGrpcService<GrpcNetBenchmarkService>()
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown gRPC stack."),
        };
}

public sealed class GrpcBenchServer : IDisposable
{
    private readonly TestServer server;

    private GrpcBenchServer(string name, TestServer server)
    {
        Name = name;
        this.server = server;
        Client = server.CreateClient();
        Client.BaseAddress = new Uri("http://localhost/");
    }

    public string Name { get; }

    public HttpClient Client { get; }

    public static GrpcBenchServer Start(
        string name,
        Action<IServiceCollection> configureServices,
        Action<Microsoft.AspNetCore.Routing.IEndpointRouteBuilder> configureEndpoints
    )
    {
#pragma warning disable ASPDEPR004, ASPDEPR008 // Synchronous startup avoids in-process benchmark deadlock.
        var builder = new WebHostBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddRouting();
                configureServices(services);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(configureEndpoints);
            });
        var server = new GrpcBenchServer(name, new TestServer(builder));
#pragma warning restore ASPDEPR004, ASPDEPR008
        return server;
    }

    public void Dispose()
    {
        Client.Dispose();
        server.Dispose();
    }
}

public sealed class GrpcBenchClient : IAsyncDisposable
{
    private readonly Func<Task<int>> invoke;
    private readonly IDisposable owner;

    private GrpcBenchClient(Func<Task<int>> invoke, IDisposable owner)
    {
        this.invoke = invoke;
        this.owner = owner;
    }

    public static GrpcBenchClient Create(
        string stack,
        GrpcBenchScenario scenario,
        GrpcCannedResponseHandler handler
    ) =>
        stack switch
        {
            GrpcBenchStacks.NSmithy => CreateNSmithy(scenario, handler),
            GrpcBenchStacks.GrpcNet => CreateGrpcNet(scenario, handler),
            _ => throw new ArgumentOutOfRangeException(nameof(stack), stack, "Unknown gRPC stack."),
        };

    public Task<int> InvokeAsync() => invoke();

    public ValueTask DisposeAsync()
    {
        owner.Dispose();
        return ValueTask.CompletedTask;
    }

    private static GrpcBenchClient CreateNSmithy(
        GrpcBenchScenario scenario,
        HttpMessageHandler handler
    )
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new GrpcBenchmarkServiceClient(httpClient);
        return scenario.Name switch
        {
            "get-item" => new GrpcBenchClient(
                async () => (await client.GetItemAsync(new GetItemInput("item-0"))).Item.PriceCents,
                client
            ),
            "list-items-100" => new GrpcBenchClient(
                async () =>
                    (await client.ListItemsAsync(new ListItemsInput(100))).Items.Values.Count,
                client
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static GrpcBenchClient CreateGrpcNet(
        GrpcBenchScenario scenario,
        HttpMessageHandler handler
    )
    {
        var channel = GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions { HttpHandler = handler }
        );
        var client = new Bench.GrpcNet.GrpcBenchmarkService.GrpcBenchmarkServiceClient(channel);
        return scenario.Name switch
        {
            "get-item" => new GrpcBenchClient(
                async () =>
                    (await client.GetItemAsync(new Bench.GrpcNet.GetItemInput { Id = "item-0" }))
                        .Item
                        .PriceCents,
                channel
            ),
            "list-items-100" => new GrpcBenchClient(
                async () =>
                    (await client.ListItemsAsync(new Bench.GrpcNet.ListItemsInput { Count = 100 }))
                        .Items
                        .Count,
                channel
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }
}

public sealed record CapturedGrpcRequest(string MethodPath, byte[] Body);

public sealed class GrpcCannedResponseHandler(GrpcBenchScenario scenario, bool record = false)
    : HttpMessageHandler
{
    private readonly List<CapturedGrpcRequest>? captures = record ? [] : null;

    public IReadOnlyList<CapturedGrpcRequest> Captures =>
        captures ?? throw new InvalidOperationException("This handler is not recording.");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var requestBody = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (captures is not null)
        {
            captures.Add(
                new CapturedGrpcRequest(request.RequestUri?.AbsolutePath ?? "", requestBody)
            );
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version20,
            RequestMessage = request,
            Content = new ByteArrayContent(scenario.ResponseBody),
        };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/grpc+proto");
        response.TrailingHeaders.TryAddWithoutValidation("grpc-status", "0");
        return response;
    }
}

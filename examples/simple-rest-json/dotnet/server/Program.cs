using Example.Hello;
using SmithyNet.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IHelloServiceHandler, HelloHandler>();

var app = builder.Build();
app.MapHelloService();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    private const string ServiceName = "smithy-net-server";

    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new SayHelloOutput(ServiceName, $"Hello, {input.Name} from {ServiceName}.")
        );
    }

    public async Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TargetUrl);

        var client = new HelloServiceClient(
            new HttpClient(),
            new SmithyClientOptions { Endpoint = new Uri(input.TargetUrl) }
        );
        var hello = await client.SayHelloAsync(new SayHelloInput(input.Name), cancellationToken);
        return new PingOutput(ServiceName, $"Ping saw: {hello.Message}");
    }
}

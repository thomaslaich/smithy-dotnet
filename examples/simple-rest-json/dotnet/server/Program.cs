using Example.Hello;

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

    public Task<PingOutput> PingAsync(
        PingInput input,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new PingOutput(ServiceName, $"Pong, {input.Name} from {ServiceName}.")
        );
    }
}

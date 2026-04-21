using Example.Hello;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(
        5000,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1;
        }
    );
    options.ListenLocalhost(
        5001,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        }
    );
});
builder.Services.AddGrpc();
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.MapHelloServiceGrpc();
app.MapGet(
    "/",
    () =>
        "Use a gRPC client to call HelloService. Reflection is not enabled in this walking skeleton."
);
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    private const string ServiceName = "smithy-net-grpc-server";

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

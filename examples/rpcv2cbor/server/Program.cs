using Example.Hello;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();
app.MapHelloServiceHttp();
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (string.Equals(input.Name, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidName("name must not be 'error'");
        }

        return Task.FromResult(
            new SayHelloOutput("rpcv2cbor-server", $"Hello, {input.Name}!")
        );
    }
}

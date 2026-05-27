using Example.Hello;
//#if (IsGrpc)
using Microsoft.AspNetCore.Server.Kestrel.Core;
//#endif
//#if (WithDocs)
using NSmithy.Server.AspNetCore.Docs;

//#endif

var builder = WebApplication.CreateBuilder(args);

//#if (IsGrpc)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http1);
    options.ListenLocalhost(5001, o => o.Protocols = HttpProtocols.Http2);
});
builder.Services.AddGrpc();

//#endif
builder.Services.AddHelloServiceHandler<HelloHandler>();

var app = builder.Build();

//#if (WithDocs && IsRestJson1)
app.MapSmithyOpenApi();
app.MapSmithyDocs();

//#elif (WithDocs)
app.MapSmithyDocs();

//#endif
//#if (IsHttp)
app.MapHelloServiceHttp();

//#endif
//#if (IsGrpc)
app.MapHelloServiceGrpc();

//#endif
app.Run();

internal sealed class HelloHandler : IHelloServiceHandler
{
    public Task<SayHelloOutput> SayHelloAsync(
        SayHelloInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new SayHelloOutput($"Hello, {input.Name}!"));
}

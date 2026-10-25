using Example.Deployments;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDeploymentsServiceHandler<DeploymentsHandler>();

var app = builder.Build();
app.MapDeploymentsService();
await app.RunAsync();

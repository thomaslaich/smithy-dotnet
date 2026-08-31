using Example.Weather;
using NSmithy.Server.AspNetCore.Docs;
using NSmithy.Server.Mcp;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

if (args.Contains("--mcp", StringComparer.Ordinal))
{
    await RunMcpServerAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWeatherServiceHandler<WeatherHandler>();

// Export server telemetry over OTLP (defaults to http://localhost:4317, where
// grafana/otel-lgtm listens). Incoming requests carry the client's trace
// context, so server spans join the client runtime's operation traces.
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("weather-server"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddOtlpExporter());

var app = builder.Build();
app.MapSmithyOpenApi();
app.MapSmithyDocs();
app.MapWeatherService();
await app.RunAsync();

static async Task RunMcpServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // The MCP stdio transport owns stdout. Send host diagnostics to stderr so they cannot
    // corrupt the JSON-RPC message stream.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddWeatherServiceHandler<WeatherHandler>();
    builder
        .Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithSmithyTools<IWeatherServiceHandler>(handler =>
            handler.CreateWeatherServiceOperationCatalog()
        );

    await builder.Build().RunAsync();
}

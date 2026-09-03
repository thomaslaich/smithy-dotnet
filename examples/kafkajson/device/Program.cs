using Confluent.Kafka;
using Examples.Kafka.Streetlights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The device owns the StreetlightDevice contract: it EMITS LightMeasured events
// and HANDLES DimLight commands sent by controllers.
//
// It runs as a generic host using the generated hosting extensions
// (SmithyGenerateDependencyInjection=true): AddStreetlightDeviceProducer registers
// the producer as a singleton, AddStreetlightDeviceCommandConsumer runs the command
// consumer for the host lifetime, and the registered IStreetlightDeviceCommandHandler
// is resolved in a new service scope per message.
var bootstrap = args.Length > 0 ? args[0] : "localhost:9092";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStreetlightDeviceProducer(
    new ProducerConfig
    {
        BootstrapServers = bootstrap,
        BrokerAddressFamily = BrokerAddressFamily.V4,
    }
);
builder.Services.AddStreetlightDeviceCommandConsumer(
    new ConsumerConfig
    {
        BootstrapServers = bootstrap,
        GroupId = "streetlight-device",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        BrokerAddressFamily = BrokerAddressFamily.V4,
    }
);
builder.Services.AddScoped<IStreetlightDeviceCommandHandler, DimLightHandler>();
builder.Services.AddHostedService<LightMeasuredEmitter>();

Console.WriteLine(
    "[device] online — handling DimLight commands and emitting LightMeasured events. Ctrl+C to stop."
);

await builder.Build().RunAsync();
Console.WriteLine("[device] stopped.");

sealed class DimLightHandler : IStreetlightDeviceCommandHandler
{
    public Task HandleDimLightAsync(
        DimLightInput command,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine(
            $"[device] DimLight received  streetlight={command.StreetlightId} -> {command.Percentage}%"
        );
        return Task.CompletedTask;
    }
}

/// <summary>Emits a lighting measurement every few seconds via the singleton producer.</summary>
sealed class LightMeasuredEmitter(StreetlightDeviceProducer producer) : BackgroundService
{
    private const string StreetlightId = "streetlight-001";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rng = new Random();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var measured = new LightMeasured(
                    AppId: "device-firmware",
                    Lumens: rng.Next(0, 1000),
                    SentAt: DateTimeOffset.UtcNow,
                    StreetlightId: StreetlightId
                );
                await producer.PublishLightMeasuredAsync(measured, stoppingToken);
                Console.WriteLine($"[device] emitted LightMeasured  lumens={measured.Lumens}");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

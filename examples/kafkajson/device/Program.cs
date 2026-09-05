using Confluent.Kafka;
using Examples.Kafka.Streetlights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Messaging.Kafka;

// The device owns the StreetlightDevice contract: it EMITS LightMeasured events
// and HANDLES DimLight commands sent by controllers.
//
var bootstrap = args.Length > 0 ? args[0] : "localhost:9092";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddKafkaMessaging(
    new KafkaMessagingOptions
    {
        Producer = new ProducerConfig
        {
            BootstrapServers = bootstrap,
            BrokerAddressFamily = BrokerAddressFamily.V4,
        },
        Consumer = new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = "streetlight-device",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            BrokerAddressFamily = BrokerAddressFamily.V4,
        },
    }
);
builder.Services.AddStreetlightDeviceEventPublisher();
builder.Services.AddStreetlightDeviceCommandConsumer();
builder.Services.AddScoped<IDimLightHandler, DimLightHandler>();
builder.Services.AddHostedService<LightMeasuredEmitter>();

Console.WriteLine(
    "[device] online — handling DimLight commands and emitting LightMeasured events. Ctrl+C to stop."
);

await builder.Build().RunAsync();
Console.WriteLine("[device] stopped.");

sealed class DimLightHandler : IDimLightHandler
{
    public Task HandleAsync(DimLightInput command, CancellationToken cancellationToken = default)
    {
        Console.WriteLine(
            $"[device] DimLight received  streetlight={command.StreetlightId} -> {command.Percentage}%"
        );
        return Task.CompletedTask;
    }
}

/// <summary>Emits a lighting measurement every few seconds via the event publisher.</summary>
sealed class LightMeasuredEmitter(IStreetlightDeviceEventPublisher publisher) : BackgroundService
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
                await publisher.PublishLightMeasuredAsync(measured, stoppingToken);
                Console.WriteLine($"[device] emitted LightMeasured  lumens={measured.Lumens}");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

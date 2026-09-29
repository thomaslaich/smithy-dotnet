using Confluent.Kafka;
using Examples.Kafka.Streetlights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Messaging.Kafka;

// The controller sends commands to the owner and handles the owner's events.
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
            GroupId = "streetlight-controller",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            BrokerAddressFamily = BrokerAddressFamily.V4,
        },
    }
);
builder.Services.AddStreetlightDeviceClient();
builder.Services.AddStreetlightDeviceEventConsumer();
builder.Services.AddScoped<IConsumeLightingEventsHandler, LightMeasuredHandler>();
builder.Services.AddHostedService<Dimmer>();

Console.WriteLine("[controller] watching events and dimming the light. Ctrl+C to stop.");
await builder.Build().RunAsync();

sealed class Dimmer(IStreetlightDeviceClient client) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int[] levels = [80, 50, 20, 0];
        foreach (var percentage in levels)
        {
            await client.DimLightAsync(
                new DimLightInput(
                    Percentage: percentage,
                    SentAt: DateTimeOffset.UtcNow,
                    StreetlightId: "streetlight-001"
                ),
                stoppingToken
            );
            Console.WriteLine($"[controller] sent DimLight -> {percentage}%");
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}

sealed class LightMeasuredHandler : IConsumeLightingEventsHandler
{
    public Task HandleAsync(
        LightMeasuredStream message,
        CancellationToken cancellationToken = default
    )
    {
        if (message is LightMeasuredStream.LightMeasured measured)
        {
            var value = measured.Value;
            Console.WriteLine(
                $"[controller] LightMeasured streetlight={value.StreetlightId} lumens={value.Lumens} at={value.SentAt:HH:mm:ss}"
            );
        }
        return Task.CompletedTask;
    }
}

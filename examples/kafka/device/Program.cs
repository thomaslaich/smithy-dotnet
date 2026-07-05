using Confluent.Kafka;
using Examples.Kafka.Streetlights;

// The device owns the StreetlightDevice contract: it EMITS LightMeasured events
// and HANDLES DimLight commands sent by controllers.
var bootstrap = args.Length > 0 ? args[0] : "localhost:9092";
const string streetlightId = "streetlight-001";

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var producerConfig = new ProducerConfig
{
    BootstrapServers = bootstrap,
    BrokerAddressFamily = BrokerAddressFamily.V4,
};
var consumerConfig = new ConsumerConfig
{
    BootstrapServers = bootstrap,
    GroupId = "streetlight-device",
    AutoOffsetReset = AutoOffsetReset.Earliest,
    BrokerAddressFamily = BrokerAddressFamily.V4,
};

await using var producer = new StreetlightDeviceProducer(producerConfig);
await using var commands = new StreetlightDeviceCommandConsumer(consumerConfig, new DimLightHandler());

Console.WriteLine(
    "[device] online — handling DimLight commands and emitting LightMeasured events. Ctrl+C to stop."
);

// Handle incoming dim commands in the background.
var handling = commands.RunAsync(cts.Token);

// Emit a lighting measurement every few seconds.
var rng = new Random();
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        var measured = new LightMeasured(
            AppId: "device-firmware",
            Lumens: rng.Next(0, 1000),
            SentAt: DateTimeOffset.UtcNow,
            StreetlightId: streetlightId
        );
        await producer.PublishLightMeasuredAsync(measured, cts.Token);
        Console.WriteLine($"[device] emitted LightMeasured  lumens={measured.Lumens}");
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
    }
}
catch (OperationCanceledException) { }

await handling;
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

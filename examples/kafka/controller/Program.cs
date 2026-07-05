using Confluent.Kafka;
using Examples.Kafka.Streetlights;

// A controller is a CLIENT of the StreetlightDevice contract: it PRODUCES DimLight
// commands and CONSUMES the LightMeasured events the device emits.
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
    GroupId = "streetlight-controller",
    AutoOffsetReset = AutoOffsetReset.Earliest,
    BrokerAddressFamily = BrokerAddressFamily.V4,
};

await using var producer = new StreetlightDeviceProducer(producerConfig);
await using var events = new StreetlightDeviceEventConsumer(consumerConfig, new LightMeasuredHandler());

Console.WriteLine(
    "[controller] watching LightMeasured events and dimming the light. Ctrl+C to stop."
);

// Watch the device's lighting events in the background.
var watching = events.RunAsync(cts.Token);

// Walk the light down in steps.
int[] levels = [80, 50, 20, 0];
try
{
    foreach (var pct in levels)
    {
        if (cts.Token.IsCancellationRequested)
            break;
        await producer.DimLightAsync(
            new DimLightInput(
                Percentage: pct,
                SentAt: DateTimeOffset.UtcNow,
                StreetlightId: streetlightId
            ),
            cts.Token
        );
        Console.WriteLine($"[controller] sent DimLight -> {pct}%");
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
    }

    // Keep watching events until Ctrl+C.
    await watching;
}
catch (OperationCanceledException) { }

Console.WriteLine("[controller] stopped.");

sealed class LightMeasuredHandler : IStreetlightDeviceEventHandler
{
    public Task HandleLightMeasuredAsync(
        LightMeasured message,
        CancellationToken cancellationToken = default
    )
    {
        Console.WriteLine(
            $"[controller] LightMeasured  streetlight={message.StreetlightId} lumens={message.Lumens} at={message.SentAt:HH:mm:ss}"
        );
        return Task.CompletedTask;
    }
}

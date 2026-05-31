using Example.Metrics;
using Grpc.Net.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5002";

using var channel = GrpcChannel.ForAddress(endpoint);
var client = new MetricsServiceGrpcClient(channel);

using var cts = new CancellationTokenSource();

// ── Server streaming ───────────────────────────────────────────────────────────

Console.WriteLine("=== StreamMetrics (server streaming — cpu.* prefix, 6 samples) ===");
var streamInput = new StreamMetricsInput(prefix: "cpu", maxSamples: 6);
await foreach (var reading in client.StreamMetricsAsync(streamInput, cts.Token))
    Console.WriteLine($"  {reading.Name, 20}  {reading.Value, 10:F2}  {reading.Unit}");

Console.WriteLine();

// ── Client streaming ───────────────────────────────────────────────────────────

Console.WriteLine("=== RecordMetrics (client streaming — upload 5 readings) ===");

static async IAsyncEnumerable<RecordMetricsInput> FakeReadings(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
)
{
    var readings = new[]
    {
        new RecordMetricsInput("cpu.usage", "percent", 42.5),
        new RecordMetricsInput("memory.free_mb", "mb", 768.0),
        new RecordMetricsInput("disk.read_mb", "mb/s", 55.3),
        new RecordMetricsInput("net.rx_mb", "mb/s", 12.1),
        new RecordMetricsInput("req.rate", "req/s", 340.0),
    };
    foreach (var r in readings)
    {
        yield return r;
        await Task.Delay(100, ct);
    }
}

var recordResult = await client.RecordMetricsAsync(FakeReadings(cts.Token), cts.Token);
Console.WriteLine($"  Server recorded {recordResult.RecordedCount} readings");

Console.WriteLine();

// ── Bidirectional streaming ────────────────────────────────────────────────────

Console.WriteLine("=== MonitorMetrics (bidi streaming — switch filter mid-stream) ===");

// Send filter updates: start with "net", then switch to "req" after a pause.
static async IAsyncEnumerable<MonitorMetricsInput> FilterUpdates(
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default
)
{
    yield return new MonitorMetricsInput("net");
    await Task.Delay(600, ct);
    yield return new MonitorMetricsInput("req");
    await Task.Delay(600, ct);
}

// Limit how long we listen.
using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
monitorCts.CancelAfter(TimeSpan.FromSeconds(3));

try
{
    await foreach (
        var reading in client.MonitorMetricsAsync(FilterUpdates(monitorCts.Token), monitorCts.Token)
    )
        Console.WriteLine($"  {reading.Name, 20}  {reading.Value, 10:F2}  {reading.Unit}");
}
catch (OperationCanceledException)
{ /* timed out — expected */
}

Console.WriteLine();
Console.WriteLine("Done.");

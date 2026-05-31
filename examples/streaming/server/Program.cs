using Example.Metrics;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(
        5002,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        }
    );
});
builder.Services.AddGrpc();
builder.Services.AddMetricsServiceHandler<MetricsHandler>();

var app = builder.Build();
app.MapMetricsServiceGrpc();
app.Run();

internal sealed class MetricsHandler : IMetricsServiceHandler
{
    // Fake metric series emitted on a fixed schedule.
    private static readonly (string Name, Func<double> Value, string Unit)[] _series =
    [
        ("cpu.usage", () => 20 + Random.Shared.NextDouble() * 60, "percent"),
        ("memory.free_mb", () => 512 + Random.Shared.NextDouble() * 1024, "mb"),
        ("disk.read_mb", () => Random.Shared.NextDouble() * 200, "mb/s"),
        ("disk.write_mb", () => Random.Shared.NextDouble() * 100, "mb/s"),
        ("net.rx_mb", () => Random.Shared.NextDouble() * 50, "mb/s"),
        ("net.tx_mb", () => Random.Shared.NextDouble() * 20, "mb/s"),
        ("req.rate", () => 100 + Random.Shared.NextDouble() * 900, "req/s"),
        ("req.latency_ms", () => 1 + Random.Shared.NextDouble() * 50, "ms"),
    ];

    // ── Server streaming ───────────────────────────────────────────────────────

    public async IAsyncEnumerable<StreamMetricsOutput> StreamMetricsAsync(
        StreamMetricsInput input,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        var prefix = input.Prefix ?? "";
        var maxSamples = input.MaxSamples ?? 0;
        var emitted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var (name, valueFn, unit) in _series)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                yield return new StreamMetricsOutput(name, unit, valueFn());
                emitted++;

                if (maxSamples > 0 && emitted >= maxSamples)
                    yield break;

                await Task.Delay(100, cancellationToken);
            }
        }
    }

    // ── Client streaming ───────────────────────────────────────────────────────

    public async Task<RecordMetricsOutput> RecordMetricsAsync(
        IAsyncEnumerable<RecordMetricsInput> input,
        CancellationToken cancellationToken = default
    )
    {
        int count = 0;
        await foreach (var reading in input.WithCancellation(cancellationToken))
        {
            Console.WriteLine($"  [record] {reading.Name} = {reading.Value} {reading.Unit}");
            count++;
        }
        return new RecordMetricsOutput(count);
    }

    // ── Bidirectional streaming ────────────────────────────────────────────────

    public async IAsyncEnumerable<MonitorMetricsOutput> MonitorMetricsAsync(
        IAsyncEnumerable<MonitorMetricsInput> input,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        // Current filter prefix — updated as client sends new MonitorMetricsInput messages.
        var prefix = "";
        var filterLock = new object();

        // Consume filter updates in the background.
        var filterTask = Task.Run(
            async () =>
            {
                await foreach (var msg in input.WithCancellation(cancellationToken))
                {
                    lock (filterLock)
                        prefix = msg.Prefix ?? "";
                }
            },
            cancellationToken
        );

        // Stream metrics that match the current filter.
        while (!cancellationToken.IsCancellationRequested && !filterTask.IsCompleted)
        {
            foreach (var (name, valueFn, unit) in _series)
            {
                if (cancellationToken.IsCancellationRequested || filterTask.IsCompleted)
                    yield break;

                string currentPrefix;
                lock (filterLock)
                    currentPrefix = prefix;

                if (!name.StartsWith(currentPrefix, StringComparison.Ordinal))
                    continue;

                yield return new MonitorMetricsOutput(name, unit, valueFn());
                await Task.Delay(150, cancellationToken);
            }
        }

        await filterTask;
    }
}

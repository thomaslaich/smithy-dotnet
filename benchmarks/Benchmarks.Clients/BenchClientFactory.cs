namespace Bench.Clients;

/// <summary>
/// The registry of clients under measurement.
/// </summary>
/// <remarks>
/// Takes a raw <see cref="HttpMessageHandler"/> rather than a server, which is
/// what lets the same registry serve both surfaces: the parity gate hands it a
/// recording handler wrapping the shared reference server, and the benchmarks
/// hand it a <see cref="StubTransport"/> with no server at all.
/// </remarks>
public static class BenchClientFactory
{
    public const string HandWritten = "hand-written";
    public const string NSmithy = "nsmithy";
    public const string NSwag = "nswag";

    /// <summary>Every client, in a stable order, with the ceiling first.</summary>
    public static IReadOnlyList<string> Names { get; } = [HandWritten, NSmithy, NSwag];

    /// <summary>
    /// Builds a client over the given transport. The base address is a
    /// placeholder, nothing resolves DNS, since neither surface uses sockets.
    /// </summary>
    public static IBenchClient Create(string name, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var httpClient = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost/"),
        };

        return name switch
        {
            HandWritten => new HandWrittenBenchClient(httpClient),
            NSmithy => new NSmithyBenchClient(httpClient),
            NSwag => new NSwagBenchClient(httpClient),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown client."),
        };
    }
}

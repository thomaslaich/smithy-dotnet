using System.Net;
using System.Text;
using Bench.Clients;
using Bench.Hosting;
using BenchmarkDotNet.Attributes;

namespace Bench.Micro;

/// <summary>
/// The client suite: request building and response parsing, with no server in
/// the measurement.
/// </summary>
/// <remarks>
/// The work under test is request serialization plus <c>@httpLabel</c> /
/// <c>@httpQuery</c> / <c>@httpHeader</c> binding, and response deserialization ,
/// per-shape code the server benchmarks never touch. Responses come from a
/// <see cref="StubTransport"/>, its canned bytes captured from the reference
/// server at setup, so they are real without being paid for per iteration. The
/// parity gate proves these clients emit byte-identical requests first.
/// </remarks>
[MemoryDiagnoser]
public class ClientBenchmarks : IDisposable
{
    private StubTransport transport = null!;
    private IBenchClient client = null!;
    private Func<IBenchClient, Task<string>> invoke = null!;

    [ParamsSource(nameof(ClientNames))]
    public string Client { get; set; } = BenchClientFactory.NSmithy;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = "";

    public static IEnumerable<string> ClientNames => BenchClientFactory.Names;

    public static IEnumerable<string> ScenarioNames => BenchClientScenarios.All.Select(s => s.Name);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        invoke = BenchClientScenarios.ByName(Scenario).Invoke;

        // Capture a real response once, then throw the server away. Which client
        // does the capturing does not matter: the parity gate proves they all
        // send the same request, so the server returns the same bytes either way.
        StubResponse canned;
        await using (var server = await BenchStacks.StartNSmithyAsync())
        {
            using var capturing = new ResponseCapturingHandler(server.CreateHandler());
            await using var probe = BenchClientFactory.Create(Client, capturing);
            await invoke(probe);
            canned =
                capturing.Captured
                ?? throw new InvalidOperationException(
                    $"Scenario '{Scenario}' produced no response to cache."
                );
        }

        transport = new StubTransport(_ => canned);
        client = BenchClientFactory.Create(Client, transport);

        // Fail loudly rather than benchmarking a broken path.
        await invoke(client);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await client.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        transport?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One client call: build the request, parse the response.</summary>
    /// <returns>The normalized result, so the JIT cannot elide the parse.</returns>
    [Benchmark]
    public Task<string> Call() => invoke(client);
}

/// <summary>
/// Focused minimal-operation comparison for attributing fixed client invocation overhead without
/// running every client/scenario combination in the full suite.
/// </summary>
[MemoryDiagnoser]
public class ClientCeremonyBenchmarks : IDisposable
{
    private static readonly StubResponse GetItemResponse = new(
        HttpStatusCode.OK,
        Encoding.UTF8.GetBytes(
            "{\"itemId\":\"item-00042\",\"name\":\"Benchmark Item 42 \\u2014 consumables\",\"priceCents\":1753,\"inStock\":false}"
        )
    );

    private StubTransport transport = null!;
    private IBenchClient client = null!;
    private Func<IBenchClient, Task<string>> invoke = null!;

    [Params(BenchClientFactory.HandWritten, BenchClientFactory.NSmithy)]
    public string Client { get; set; } = BenchClientFactory.HandWritten;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        invoke = BenchClientScenarios.ByName("client-get-item").Invoke;
        transport = new StubTransport(_ => GetItemResponse);
        client = BenchClientFactory.Create(Client, transport);
        await invoke(client);
    }

    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        await client.DisposeAsync();
        Dispose();
    }

    public void Dispose()
    {
        transport?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public Task<string> Call() => invoke(client);
}

/// <summary>Records the first response that passes through, then forwards it.</summary>
internal sealed class ResponseCapturingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public StubResponse? Captured { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (Captured is not null)
            return response;

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

        // Response headers bound by the model (x-total-count on SearchItems) have
        // to survive into the canned response, or the client would parse a
        // different shape than it does against the real server.
        var extra = response.Headers.Select(h => (h.Key, string.Join(", ", h.Value))).ToArray();

        Captured = new StubResponse(
            (HttpStatusCode)(int)response.StatusCode,
            body,
            contentType,
            extra
        );

        // Hand back a fresh response: the body stream has been consumed.
        var replacement = new HttpResponseMessage(response.StatusCode)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(body),
        };
        replacement.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        foreach (var (name, value) in extra)
            replacement.Headers.TryAddWithoutValidation(name, value);

        response.Dispose();
        return replacement;
    }
}

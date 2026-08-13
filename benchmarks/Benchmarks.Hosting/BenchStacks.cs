using System.Text.Json;
using Bench.Stacks;
using Bench.Stacks.MinimalApi;
using Bench.Stacks.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Nsmithy.Bench;

namespace Bench.Hosting;

/// <summary>
/// The registry of server stacks under measurement.
/// </summary>
/// <remarks>
/// Adding a stack here makes it visible to both the parity gate and the server
/// pipeline benchmarks at once, which is the point, a stack that is benchmarked
/// but not parity-checked would be reporting numbers for a contract nobody
/// verified it serves.
/// </remarks>
public static class BenchStacks
{
    public const string NSmithy = "nsmithy";
    public const string MinimalApi = "minimal-api";
    public const string Mvc = "mvc";

    /// <summary>
    /// Every stack, keyed by name, in a stable order. The reference stack, the
    /// one the golden captures are recorded from, comes first.
    /// </summary>
    public static IReadOnlyList<(string Name, Func<Task<BenchServer>> Start)> All { get; } =
    [(NSmithy, StartNSmithyAsync), (MinimalApi, StartMinimalApiAsync), (Mvc, StartMvcAsync)];

    /// <summary>NSmithy's generated minimal-API server over restJson1.</summary>
    public static Task<BenchServer> StartNSmithyAsync() =>
        BenchServer.StartAsync(
            NSmithy,
            builder => builder.Services.AddBenchmarkServiceHandler<NSmithyBenchmarkHandler>(),
            app => app.MapBenchmarkService()
        );

    /// <summary>Hand-written minimal API with source-generated JSON, the ceiling.</summary>
    public static Task<BenchServer> StartMinimalApiAsync() =>
        BenchServer.StartAsync(MinimalApi, static _ => { }, app => app.MapBenchmarkMinimalApi());

    /// <summary>Hand-written MVC controller, sharing the minimal-API baseline's DTOs.</summary>
    /// <remarks>
    /// The controller lives in a referenced assembly, so its application part has
    /// to be registered explicitly, MVC only discovers controllers in the entry
    /// assembly by default, and would otherwise start with zero routes and 404
    /// every scenario.
    /// </remarks>
    public static Task<BenchServer> StartMvcAsync() =>
        BenchServer.StartAsync(
            Mvc,
            builder =>
                builder
                    .Services.AddControllers()
                    .AddApplicationPart(typeof(BenchmarkController).Assembly)
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy =
                            JsonNamingPolicy.CamelCase;
                        options.JsonSerializerOptions.TypeInfoResolverChain.Insert(
                            0,
                            MinimalApiJsonContext.Default
                        );
                    }),
            app => app.MapControllers()
        );

    /// <summary>Starts a stack by name.</summary>
    public static Task<BenchServer> StartAsync(string name) =>
        All.FirstOrDefault(s => s.Name == name).Start?.Invoke()
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown stack.");
}

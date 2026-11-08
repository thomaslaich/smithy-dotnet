using System.Text.Encodings.Web;
using System.Text.Json;
using Bench.Corpus;
using Bench.Stacks;
using Bench.Stacks.MinimalApi;
using Bench.Stacks.Mvc;
using Bench.Stacks.TypeSpec;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Nsmithy.Bench;
using NSmithyBenchmarkService;
using NSmithyBenchmarkService.Controllers;
using TypeSpec.Helpers;

namespace Bench.Hosting;

/// <summary>
/// One server stack under measurement.
/// </summary>
/// <param name="Name">Stack name, used as a BenchmarkDotNet parameter and in parity output.</param>
/// <param name="Start">Starts the stack over the in-memory transport.</param>
/// <param name="Unserved">
/// Corpus scenarios the stack cannot serve byte-identically. The parity gate
/// checks that each of these really does differ, and the server suite skips
/// them, so a stack is compared only on the scenarios it serves.
/// </param>
public sealed record StackDefinition(
    string Name,
    Func<Task<BenchServer>> Start,
    IReadOnlySet<string> Unserved
)
{
    public bool Serves(string scenario) => !Unserved.Contains(scenario);
}

/// <summary>
/// The registry of server stacks under measurement.
/// </summary>
public static class BenchStacks
{
    public const string NSmithy = "nsmithy";
    public const string MinimalApi = "minimal-api";
    public const string Mvc = "mvc";
    public const string TypeSpec = "typespec";

    private static readonly IReadOnlySet<string> ServesEverything = new HashSet<string>();

    /// <summary>
    /// What <c>@typespec/http-server-csharp</c> 0.58.0-alpha.33 gets wrong:
    /// response header bindings are serialized into the body as a wrapper
    /// property, the status code is serialized as a body property, and
    /// constraint violations are reported as MVC problem details rather than
    /// the modeled <c>ValidationException</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> TypeSpecUnserved = new HashSet<string>
    {
        BenchCorpus.GetItemMiss.Name,
        BenchCorpus.SearchItems.Name,
        BenchCorpus.CreateOrderSmall.Name,
        BenchCorpus.CreateOrderLarge.Name,
        BenchCorpus.ValidationRange.Name,
        BenchCorpus.ValidationPattern.Name,
        BenchCorpus.ValidationMulti.Name,
        BenchCorpus.ValidationLength.Name,
    };

    /// <summary>
    /// Every stack, in a stable order. The reference stack, the one the golden
    /// captures are recorded from, comes first.
    /// </summary>
    public static IReadOnlyList<StackDefinition> All { get; } =
    [
        new(NSmithy, StartNSmithyAsync, ServesEverything),
        new(MinimalApi, StartMinimalApiAsync, ServesEverything),
        new(Mvc, StartMvcAsync, ServesEverything),
        new(TypeSpec, StartTypeSpecAsync, TypeSpecUnserved),
    ];

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

    /// <summary>
    /// The MVC controllers <c>@typespec/http-server-csharp</c> generates, hosted
    /// the way its own generated <c>Program.cs</c> hosts them.
    /// </summary>
    public static Task<BenchServer> StartTypeSpecAsync() =>
        BenchServer.StartAsync(
            TypeSpec,
            builder =>
            {
                builder.Services.AddSingleton<
                    InSmithyBenchmarkServiceOperations,
                    TypeSpecBenchmarkOperations
                >();
                builder
                    .Services.AddControllers(options =>
                        options.Filters.Add<HttpServiceExceptionFilter>()
                    )
                    .AddApplicationPart(
                        typeof(NSmithyBenchmarkServiceOperationsController).Assembly
                    )
                    // MVC's default encoder leaves non-ASCII unescaped; every other
                    // stack escapes it, so this is what puts the same bytes on the wire.
                    .AddJsonOptions(options =>
                        options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Default
                    );
            },
            app => app.MapControllers()
        );

    /// <summary>Looks a stack up by name.</summary>
    public static StackDefinition ByName(string name) =>
        All.FirstOrDefault(s => s.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown stack.");

    /// <summary>Starts a stack by name.</summary>
    public static Task<BenchServer> StartAsync(string name) => ByName(name).Start();
}

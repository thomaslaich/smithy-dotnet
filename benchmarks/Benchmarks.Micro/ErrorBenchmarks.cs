using Bench.Domain;
using BenchmarkDotNet.Attributes;
using Nsmithy.Bench;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;

namespace Bench.Micro;

/// <summary>
/// The error suite: serializing a modeled error response, with no ASP.NET in the
/// measurement.
/// </summary>
/// <remarks>
/// The server suite already posts <c>get-item-miss</c> and the four validation
/// scenarios, so error responses are measured end to end. What it cannot say is
/// how much of that belongs to error serialization rather than to routing, model
/// binding, or the handler. This isolates it.
/// <para>
/// The baseline is the <em>success</em> response on the same operation, through
/// the same protocol object generated code holds in a static field. That makes
/// the ratio read directly as "what a modeled error costs relative to a success",
/// and it is a conservative comparison: <c>GetItemOutput</c> serializes four
/// members, <c>ItemNotFound</c> only two, so the error path is writing a
/// <em>smaller</em> payload. Any excess is overhead, not bytes.
/// </para>
/// <para>
/// This exists because <c>RestProtocol.SerializeStructuredError</c> re-derived
/// per response what the success path resolves once: a <c>(dynamic)</c> dispatch,
/// a walk over the schema's members, and two dictionaries. The rpcv2Cbor equivalent is worse still — it
/// constructs a fresh <c>CborWriterCompiler</c> and recompiles the error shape's
/// whole writer tree per response — but rpcv2Cbor has no coverage in this suite,
/// so it stays unmeasured.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ErrorBenchmarks
{
    // Built exactly as the generated server builds them: one service protocol,
    // one operation protocol, both resolved once into static fields. Anything the
    // error path still redoes per call is therefore genuinely per call.
    private static readonly IServiceProtocol ServiceProtocol = new RestJson1Protocol().ForService(
        BenchmarkServiceSchema.Schema
    );

    private static readonly IServerOperationProtocol<GetItemInput, GetItemOutput> GetItem =
        ServiceProtocol.ForServerOperation(GetItemSchema.Schema);

    private GetItemOutput success = null!;
    private ItemNotFound error = null!;

    [GlobalSetup]
    public void Setup()
    {
        // The same values the corpus drives through the stacks: get-item-hit
        // resolves item-00042, get-item-miss fails on item-99999. Constructed the
        // way NSmithyBenchmarkHandler constructs them, so this measures the same
        // objects the server suite does.
        var item =
            BenchDomain.GetItem("item-00042")
            ?? throw new InvalidOperationException("Corpus item 'item-00042' is missing.");

        success = new GetItemOutput(
            InStock: item.InStock,
            ItemId: item.ItemId,
            Name: item.Name,
            PriceCents: item.PriceCents
        );
        error = new ItemNotFound("No item with id 'item-99999'.", "item-99999");
    }

    [Benchmark(Baseline = true, Description = "success response")]
    public SmithyHttpServerResponse Success() => GetItem.SerializeResponse(success);

    [Benchmark(Description = "modeled error response")]
    public SmithyHttpServerResponse Error()
    {
        if (!GetItem.TrySerializeError(error, out var response))
        {
            throw new InvalidOperationException(
                "ItemNotFound is a modeled error of GetItem; TrySerializeError must handle it."
            );
        }

        return response;
    }
}

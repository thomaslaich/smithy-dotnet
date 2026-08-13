using System.Globalization;

namespace Bench.Stacks.NSwagGen;

/// <summary>
/// Captures response headers the generated client would otherwise discard.
/// </summary>
/// <remarks>
/// NSwag's generated operations return only the response body, so a modelled
/// response header, <c>x-total-count</c> on SearchItems, is unreachable through
/// the normal API. <c>ProcessResponse</c> is NSwag's own documented extension
/// point for exactly this, so implementing it here reaches the header without
/// editing a line of generated code.
/// <para>
/// This is a genuine capability gap rather than a benchmark inconvenience: out of
/// the box, an NSwag client cannot see that header at all. It is recorded here so
/// the client can pass the parity gate, and disclosed rather than hidden.
/// </para>
/// </remarks>
public partial class NSwagBenchmarkClient
{
    /// <summary>The <c>x-total-count</c> value from the most recent response.</summary>
    public int LastTotalCount { get; private set; }

    partial void ProcessResponse(HttpClient client, HttpResponseMessage response)
    {
        if (
            response.Headers.TryGetValues("x-total-count", out var values)
            && int.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed
            )
        )
        {
            LastTotalCount = parsed;
        }
    }
}

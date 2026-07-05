using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NSmithy.Client;

/// <summary>
/// OpenTelemetry-friendly instrumentation sources for the client runtime. Subscribe with an
/// <see cref="ActivityListener"/> / OpenTelemetry tracer provider using
/// <see cref="ActivitySourceName"/>, and a meter provider using <see cref="MeterName"/>.
/// Span and metric dimensions use Smithy identifiers: <c>rpc.system</c> = <c>smithy</c>,
/// <c>rpc.service</c> = the service shape id, <c>rpc.method</c> = the operation name.
/// </summary>
public static class SmithyClientTelemetry
{
    public const string ActivitySourceName = "NSmithy.Client";

    public const string MeterName = "NSmithy.Client";

    private static readonly string? Version = typeof(SmithyClientTelemetry)
        .Assembly.GetName()
        .Version?.ToString();

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal static readonly Meter Meter = new(MeterName, Version);

    /// <summary>Total transport attempts, including retries.</summary>
    internal static readonly Counter<long> Attempts = Meter.CreateCounter<long>(
        "smithy.client.attempts",
        unit: "{attempt}",
        description: "Total transport attempts, including retries."
    );

    /// <summary>Failed operation executions, dimensioned by error.type.</summary>
    internal static readonly Counter<long> Errors = Meter.CreateCounter<long>(
        "smithy.client.errors",
        unit: "{error}",
        description: "Failed operation executions."
    );

    /// <summary>End-to-end operation duration, spanning all attempts and backoff.</summary>
    internal static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "smithy.client.operation.duration",
        unit: "s",
        description: "End-to-end operation duration, spanning all attempts and backoff."
    );
}

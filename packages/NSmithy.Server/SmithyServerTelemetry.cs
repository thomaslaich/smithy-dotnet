using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NSmithy.Server;

/// <summary>
/// OpenTelemetry-friendly instrumentation sources for generated servers, the dual of
/// <c>SmithyClientTelemetry</c>. Dimensions use Smithy identifiers: <c>rpc.system</c> =
/// <c>smithy</c>, <c>rpc.service</c> = the service shape id, <c>rpc.method</c> = the operation name.
/// </summary>
/// <remarks>
/// <para>
/// The host owns the request. The span it records for one, such as ASP.NET Core's or the MCP
/// SDK's, carries those attributes and, when the call fails, <c>error.type</c>, whether or not
/// either source here is subscribed: the application opted in when it recorded the span.
/// </para>
/// <para>
/// Subscribing to <see cref="ActivitySourceName"/> adds an internal span around each handler
/// call. Subscribing to <see cref="MeterName"/> records <c>smithy.server.operation.duration</c>,
/// which the host measures from reading the request to writing the last of the response.
/// </para>
/// </remarks>
public static class SmithyServerTelemetry
{
    public const string ActivitySourceName = "NSmithy.Server";

    public const string MeterName = "NSmithy.Server";

    private static readonly string? Version = typeof(SmithyServerTelemetry)
        .Assembly.GetName()
        .Version?.ToString();

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal static readonly Meter Meter = new(MeterName, Version);

    /// <summary>Operation duration, from reading the request to writing the response.</summary>
    internal static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "smithy.server.operation.duration",
        unit: "s",
        description: "Operation duration, from reading the request to writing the response."
    );

    /// <summary>Names the call on <paramref name="activity"/>.</summary>
    internal static void Describe(Activity activity, ServerOperationIdentity operation)
    {
        activity.SetTag("rpc.system", "smithy");
        activity.SetTag("rpc.service", operation.ServiceTag);
        activity.SetTag("rpc.method", operation.Operation.Name);
    }

    /// <summary>
    /// Ends a call a host served: <paramref name="error"/>, if any, on the request's span, and the
    /// call's duration since <paramref name="startTimestamp"/>.
    /// </summary>
    internal static void Complete(
        ServerOperationIdentity operation,
        Activity? request,
        long startTimestamp,
        Exception? error
    )
    {
        var errorType = error is null ? null : ErrorType(error);
        if (errorType is not null)
        {
            request?.SetTag("error.type", errorType);
        }

        // Guarded so the unsubscribed path skips the elapsed time and the tags.
        if (!OperationDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { "rpc.system", "smithy" },
            { "rpc.service", operation.ServiceTag },
            { "rpc.method", operation.Operation.Name },
        };
        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        OperationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>
    /// The nearest recorded server span from <paramref name="activity"/> up, so a span middleware
    /// starts inside the request does not hide the request's own.
    /// </summary>
    internal static Activity? ServerSpan(Activity? activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.Kind == ActivityKind.Server)
            {
                return current.IsAllDataRequested ? current : null;
            }
        }

        return null;
    }

    // As the client names it, so a call's two sides agree.
    internal static string ErrorType(Exception exception) =>
        exception.GetType().FullName ?? exception.GetType().Name;
}

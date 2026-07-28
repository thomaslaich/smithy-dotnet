using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

/// <summary>
/// ActivitySource/Meter are process-global and other test classes run in parallel, so every
/// test here uses a unique service shape id and filters captured telemetry by it.
/// </summary>
public sealed class SmithyClientTelemetryTests : IDisposable
{
    private readonly object sync = new();
    private readonly List<Activity> activities = [];
    private readonly List<(
        string Instrument,
        double Value,
        KeyValuePair<string, object?>[] Tags
    )> measurements = [];
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;

    public SmithyClientTelemetryTests()
    {
        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SmithyClientTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (sync)
                {
                    activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SmithyClientTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags)
        );
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags)
        );
        meterListener.Start();
    }

    private void Record(
        string instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        var copied = tags.ToArray();
        lock (sync)
        {
            measurements.Add((instrument, value, copied));
        }
    }

    public void Dispose()
    {
        activityListener.Dispose();
        meterListener.Dispose();
    }

    [Fact]
    public async Task SuccessfulOperationEmitsSpanAttemptAndDuration()
    {
        var serviceId = "example.telemetry#WeatherSuccess";
        var runtime = new SmithyClientRuntime(new StaticTransport(Ok()));

        await runtime.InvokeAsync(Binding(serviceId), "input");

        var operation = Assert.Single(
            Activities(a => a.OperationName == "WeatherSuccess.GetForecast")
        );
        Assert.Equal(ActivityKind.Client, operation.Kind);
        Assert.Equal("smithy", operation.GetTagItem("rpc.system"));
        Assert.Equal(serviceId, operation.GetTagItem("rpc.service"));
        Assert.Equal("GetForecast", operation.GetTagItem("rpc.method"));
        Assert.NotEqual(ActivityStatusCode.Error, operation.Status);

        var attempt = Assert.Single(Activities(a => a.ParentSpanId == operation.SpanId));
        Assert.Equal("attempt", attempt.OperationName);
        Assert.Equal(1, attempt.GetTagItem("smithy.attempt"));

        Assert.Equal(1, Total("smithy.client.attempts", serviceId));
        Assert.Equal(0, Total("smithy.client.errors", serviceId));
        Assert.Single(Values("smithy.client.operation.duration", serviceId));
    }

    [Fact]
    public async Task FailedOperationMarksSpanAndCountsError()
    {
        var serviceId = "example.telemetry#WeatherFailure";
        var runtime = new SmithyClientRuntime(
            new StaticTransport(Response(HttpStatusCode.InternalServerError))
        );

        await Assert.ThrowsAsync<SmithyClientException>(() =>
            runtime.InvokeAsync(Binding(serviceId), "input")
        );

        var operation = Assert.Single(
            Activities(a => a.OperationName == "WeatherFailure.GetForecast")
        );
        Assert.Equal(ActivityStatusCode.Error, operation.Status);
        Assert.Equal(typeof(SmithyClientException).FullName, operation.GetTagItem("error.type"));

        var error = Assert.Single(Measurements("smithy.client.errors", serviceId));
        Assert.Contains(
            new KeyValuePair<string, object?>("error.type", typeof(SmithyClientException).FullName),
            error.Tags
        );
    }

    [Fact]
    public async Task RetriesEmitOneAttemptSpanPerAttempt()
    {
        var serviceId = "example.telemetry#WeatherRetry";
        var transport = new SequenceTransport(Response(HttpStatusCode.InternalServerError), Ok());
        var runtime = new SmithyClientRuntime(
            transport,
            retryStrategy: new SmithySimpleRetryStrategy(maxAttempts: 2)
        );

        await runtime.InvokeAsync(Binding(serviceId), "input");

        var operation = Assert.Single(
            Activities(a => a.OperationName == "WeatherRetry.GetForecast")
        );
        Assert.Equal(2, Activities(a => a.ParentSpanId == operation.SpanId).Count);
        Assert.Equal(2, Total("smithy.client.attempts", serviceId));
        // The execution eventually succeeded, so no error is counted.
        Assert.Equal(0, Total("smithy.client.errors", serviceId));
    }

    private List<Activity> Activities(Func<Activity, bool> predicate)
    {
        lock (sync)
        {
            return activities.Where(predicate).ToList();
        }
    }

    private List<(
        string Instrument,
        double Value,
        KeyValuePair<string, object?>[] Tags
    )> Measurements(string instrument, string serviceId)
    {
        lock (sync)
        {
            return measurements
                .Where(m =>
                    m.Instrument == instrument
                    && m.Tags.Contains(new KeyValuePair<string, object?>("rpc.service", serviceId))
                )
                .ToList();
        }
    }

    private double Total(string instrument, string serviceId) =>
        Measurements(instrument, serviceId).Sum(m => m.Value);

    private List<double> Values(string instrument, string serviceId) =>
        Measurements(instrument, serviceId).Select(m => m.Value).ToList();

    private static SmithyOperationBinding<string, string> Binding(string serviceId) =>
        new(
            ShapeId.Parse(serviceId),
            ShapeId.Parse($"{serviceId.Split('#')[0]}#GetForecast"),
            new TextProtocol()
        );

    private static SmithyHttpResponse Ok() => Response(HttpStatusCode.OK);

    private static SmithyHttpResponse Response(HttpStatusCode statusCode) =>
        new(
            statusCode,
            statusCode.ToString(),
            Encoding.UTF8.GetBytes("serialized output"),
            EmptyHeaders,
            EmptyHeaders
        );

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private sealed class StaticTransport(SmithyHttpResponse response) : IHttpTransport
    {
        public Task<SmithyHttpResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpResponseMode responseMode,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(response);
    }

    private sealed class SequenceTransport(params SmithyHttpResponse[] responses) : IHttpTransport
    {
        private int attempts;

        public Task<SmithyHttpResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            var index = Math.Min(attempts, responses.Length - 1);
            attempts++;
            return Task.FromResult(responses[index]);
        }
    }

    private sealed class TextProtocol : IClientOperationProtocol<string, string>
    {
        public SmithyHttpRequest SerializeRequest(string input) =>
            new(HttpMethod.Post, $"/{input}");

        public string DeserializeResponse(SmithyHttpResponse response) => "output";

        public bool IsErrorResponse(SmithyHttpResponse response) => (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(null);
    }
}

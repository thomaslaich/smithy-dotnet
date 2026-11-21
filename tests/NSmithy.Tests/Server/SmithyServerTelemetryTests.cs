using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Core.Validation;
using NSmithy.Http;
using NSmithy.Protocols.RestJson;
using NSmithy.Server;
using NSmithy.Server.AspNetCore;
using Nsmithy.Tests.Server;
using Nsmithy.Tests.Server.Schemas;

namespace NSmithy.Tests.Server;

/// <summary>
/// Serves CreateUser through ASP.NET Core, as an application would. ActivitySource/Meter are
/// process-global and other test classes run in parallel, so every test serves a service of its
/// own and filters captured telemetry by its id.
/// </summary>
public sealed class SmithyServerTelemetryTests : IDisposable
{
    // Stands in for middleware that starts a span of its own around the endpoint.
    private static readonly ActivitySource Middleware = new("NSmithy.Tests.Server.Middleware");

    private static readonly TimeSpan FailingStreamDelay = TimeSpan.FromMilliseconds(100);

    private readonly ServiceSchema service = Schemas.Service(
        new ShapeId("telemetry", $"Users{Guid.NewGuid():N}"),
        "1"
    );

    private readonly object sync = new();
    private readonly List<Activity> handlerSpans = [];
    private readonly List<(double Value, Dictionary<string, object?> Tags)> durations = [];
    private readonly List<IDisposable> listeners = [];
    private readonly TaskCompletionSource served = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private Activity? request;
    private Activity? middleware;

    public void Dispose()
    {
        foreach (var listener in listeners)
        {
            listener.Dispose();
        }
    }

    [Fact]
    public async Task TheRequestSpanNamesTheCallUnderAMiddlewareSpan()
    {
        Listen(smithy: false);
        await using var app = await StartAsync(new RestJson1Protocol(), Succeed, wrap: true);

        using var response = await PostAsync(app, "Ada");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(middleware);
        Assert.NotNull(request);
        Assert.Equal("smithy", request.GetTagItem("rpc.system"));
        Assert.Equal(service.Id.ToString(), request.GetTagItem("rpc.service"));
        Assert.Equal("CreateUser", request.GetTagItem("rpc.method"));
        Assert.Null(request.GetTagItem("error.type"));
        Assert.Null(middleware.GetTagItem("rpc.service"));
    }

    [Fact]
    public async Task TheHandlerRunsInAnInternalSpanUnderTheCurrentOne()
    {
        Listen(smithy: true);
        await using var app = await StartAsync(new RestJson1Protocol(), Succeed, wrap: true);

        using var response = await PostAsync(app, "Ada");

        var handler = HandlerSpan();
        Assert.Equal(ActivityKind.Internal, handler.Kind);
        Assert.Equal(middleware!.SpanId, handler.ParentSpanId);
        Assert.Equal($"{service.Id.Name}.CreateUser", handler.DisplayName);
        Assert.Equal("CreateUser", handler.GetTagItem("rpc.method"));
        Assert.Equal(ActivityStatusCode.Unset, handler.Status);
    }

    [Fact]
    public async Task ARejectedRequestNamesItsErrorAndRunsNoHandlerSpan()
    {
        Listen(smithy: true);
        await using var app = await StartAsync(new RestJson1Protocol(), Succeed);

        using var response = await PostAsync(app, "ab");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(typeof(ValidationException).FullName, request!.GetTagItem("error.type"));
        Assert.Equal(typeof(ValidationException).FullName, Duration().Tags["error.type"]);
        Assert.Empty(HandlerSpans());
    }

    [Fact]
    public async Task AModeledClientErrorIsNamedButIsNotTheHandlersFailure()
    {
        Listen(smithy: true);
        await using var app = await StartAsync(
            new RestJson1Protocol(),
            (_, _) => Task.FromException<CreateUserOutput>(new UserExists("Ada exists."))
        );

        using var response = await PostAsync(app, "Ada");

        var handler = HandlerSpan();
        Assert.Equal(typeof(UserExists).FullName, handler.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Unset, handler.Status);
        Assert.Equal(typeof(UserExists).FullName, request!.GetTagItem("error.type"));
    }

    [Fact]
    public async Task AModeledServerErrorFailsTheHandlerSpanWhateverStatusTheProtocolAnswers()
    {
        // As gRPC does: the response succeeds at the HTTP level and carries the failure elsewhere.
        Listen(smithy: true);
        await using var app = await StartAsync(
            new ShapedProtocol(Shape.ErrorsAnsweredWithSuccess),
            (_, _) => throw new UserStoreUnavailable("Down.")
        );

        using var response = await PostAsync(app, "Ada");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ActivityStatusCode.Error, HandlerSpan().Status);
        Assert.Equal(typeof(UserStoreUnavailable).FullName, request!.GetTagItem("error.type"));
    }

    [Fact]
    public async Task AnUnexpectedRequestFailureIsAFailedCall()
    {
        // As a truncated gRPC frame does: neither a missing member nor a malformed request.
        Listen(smithy: false);
        await using var app = await StartAsync(new ShapedProtocol(Shape.BrokenRequest), Succeed);

        await SendIgnoringFailureAsync(app, "Ada");

        var unexpected = typeof(InvalidOperationException).FullName;
        Assert.Equal(unexpected, request!.GetTagItem("error.type"));
        Assert.Equal(unexpected, Duration().Tags["error.type"]);
    }

    [Fact]
    public async Task AStreamedResponseIsMeasuredToItsLastByteAndItsFailure()
    {
        Listen(smithy: false);
        await using var app = await StartAsync(new ShapedProtocol(Shape.FailingStream), Succeed);

        await SendIgnoringFailureAsync(app, "Ada");

        var (value, tags) = Duration();
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["error.type"]);
        Assert.True(value >= FailingStreamDelay.TotalSeconds, $"Measured {value}s.");
    }

    [Fact]
    public async Task ASuccessfulCallRecordsItsDuration()
    {
        Listen(smithy: false);
        await using var app = await StartAsync(new RestJson1Protocol(), Succeed);

        using var response = await PostAsync(app, "Ada");

        var tags = Duration().Tags;
        Assert.Equal("smithy", tags["rpc.system"]);
        Assert.Equal("CreateUser", tags["rpc.method"]);
        Assert.False(tags.ContainsKey("error.type"));
    }

    private static Task<CreateUserOutput> Succeed(CreateUserInput input, CancellationToken _) =>
        Task.FromResult(new CreateUserOutput(input.Name));

    private async Task<WebApplication> StartAsync(
        IProtocol protocol,
        Func<CreateUserInput, CancellationToken, Task<CreateUserOutput>> handler,
        bool wrap = false
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Use(
            async (HttpContext context, RequestDelegate next) =>
            {
                request = context.Features.Get<IHttpActivityFeature>()?.Activity;
                try
                {
                    using var span = wrap ? Middleware.StartActivity("middleware") : null;
                    middleware = span;
                    await next(context);
                }
                finally
                {
                    served.TrySetResult();
                }
            }
        );
        app.MapSmithyService(
            new ServiceOperationCatalog(
                service,
                ServerOperation.Create(CreateUserSchema.Schema, _ => handler)
            ),
            [new SmithyServerProtocol("RestJson1", protocol)]
        );
        await app.StartAsync();
        return app;
    }

    // Returns once the endpoint, and the telemetry it records when it ends, has finished.
    private async Task<HttpResponseMessage> PostAsync(WebApplication app, string name)
    {
        using var client = app.GetTestClient();
        using var content = new StringContent(
            $$"""{"name":"{{name}}"}""",
            Encoding.UTF8,
            "application/json"
        );
        var response = await client.PostAsync(new Uri("/users", UriKind.Relative), content);
        await response.Content.ReadAsByteArrayAsync();
        await served.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return response;
    }

    private async Task SendIgnoringFailureAsync(WebApplication app, string name)
    {
        try
        {
            using var response = await PostAsync(app, name);
        }
        catch (Exception exception) when (exception is not TimeoutException)
        {
            // The test server surfaces the application's failure; the telemetry is under test.
            await served.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // Records ASP.NET Core's request spans, NSmithy's when smithy is set, and NSmithy's metrics.
    private void Listen(bool smithy)
    {
        var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == "Microsoft.AspNetCore"
                || source == Middleware
                || (smithy && source.Name == SmithyServerTelemetry.ActivitySourceName),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name == SmithyServerTelemetry.ActivitySourceName)
                {
                    lock (sync)
                    {
                        handlerSpans.Add(activity);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
        listeners.Add(activityListener);

        var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == SmithyServerTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<double>(
            (_, measurement, tags, _) =>
            {
                var copied = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    copied[tag.Key] = tag.Value;
                }

                if (
                    copied.TryGetValue("rpc.service", out var recorded)
                    && (string?)recorded == service.Id.ToString()
                )
                {
                    lock (sync)
                    {
                        durations.Add((measurement, copied));
                    }
                }
            }
        );
        meterListener.Start();
        listeners.Add(meterListener);
    }

    private List<Activity> HandlerSpans()
    {
        lock (sync)
        {
            return
            [
                .. handlerSpans.Where(activity =>
                    (string?)activity.GetTagItem("rpc.service") == service.Id.ToString()
                ),
            ];
        }
    }

    private Activity HandlerSpan() => Assert.Single(HandlerSpans());

    private (double Value, Dictionary<string, object?> Tags) Duration()
    {
        lock (sync)
        {
            return Assert.Single(durations);
        }
    }

    private enum Shape
    {
        ErrorsAnsweredWithSuccess,
        BrokenRequest,
        FailingStream,
    }

    // restJson1, bent into the shapes other protocols take.
    private sealed class ShapedProtocol(Shape shape) : IProtocol
    {
        public IServiceProtocol ForService(ServiceSchema service) =>
            new ServiceProtocol(new RestJson1Protocol().ForService(service), shape);

        private sealed class ServiceProtocol(IServiceProtocol inner, Shape shape) : IServiceProtocol
        {
            public IClientOperationProtocol<TInput, TOutput> ForClientOperation<TInput, TOutput>(
                OperationSchema<TInput, TOutput> operation
            ) => inner.ForClientOperation(operation);

            public IServerOperationProtocol<TInput, TOutput> ForServerOperation<TInput, TOutput>(
                OperationSchema<TInput, TOutput> operation
            ) => new Operation<TInput, TOutput>(inner.ForServerOperation(operation), shape);

            public ServerRouting GetServerRouting(IReadOnlyList<IOperationSchema> operations) =>
                inner.GetServerRouting(operations);
        }

        private sealed class Operation<TInput, TOutput>(
            IServerOperationProtocol<TInput, TOutput> inner,
            Shape shape
        ) : IServerOperationProtocol<TInput, TOutput>
        {
            public ISmithyValidator<TInput>? InputValidator => inner.InputValidator;

            public bool StreamsRequestBody => inner.StreamsRequestBody;

            public ValueTask<TInput> DeserializeRequestAsync(
                SmithyHttpRequest request,
                CancellationToken cancellationToken = default
            ) =>
                shape == Shape.BrokenRequest
                    ? throw new InvalidOperationException("Truncated frame.")
                    : inner.DeserializeRequestAsync(request, cancellationToken);

            public SmithyHttpServerResponse SerializeResponse(
                TOutput output,
                CancellationToken cancellationToken = default
            ) =>
                shape == Shape.FailingStream
                    ? new SmithyHttpServerResponse { Body = FailingBodyAsync(cancellationToken) }
                    : inner.SerializeResponse(output, cancellationToken);

            public bool TrySerializeError(
                Exception exception,
                out SmithyHttpServerResponse response
            )
            {
                if (!inner.TrySerializeError(exception, out response))
                {
                    return false;
                }

                if (shape == Shape.ErrorsAnsweredWithSuccess)
                {
                    response.Dispose();
                    response = new SmithyHttpServerResponse();
                }

                return true;
            }

            private static async IAsyncEnumerable<ReadOnlyMemory<byte>> FailingBodyAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default
            )
            {
                yield return Encoding.UTF8.GetBytes("{");
                await Task.Delay(FailingStreamDelay, cancellationToken);
                throw new InvalidOperationException("The stream broke.");
            }
        }
    }
}

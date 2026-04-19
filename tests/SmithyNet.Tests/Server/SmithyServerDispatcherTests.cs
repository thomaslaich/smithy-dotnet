using SmithyNet.Server;

namespace SmithyNet.Tests.Server;

public sealed class SmithyServerDispatcherTests
{
    [Fact]
    public async Task DispatchAsyncInvokesRegisteredOperation()
    {
        var dispatcher = new SmithyServerDispatcher();
        dispatcher.Register(
            "Weather",
            "GetForecast",
            (request, _) =>
                Task.FromResult(
                    new SmithyServerResponse(
                        request.ServiceName,
                        request.OperationName,
                        $"forecast for {request.Input}"
                    )
                )
        );

        var response = await dispatcher.DispatchAsync(
            new SmithyServerRequest("Weather", "GetForecast", "Zurich")
        );

        Assert.Equal("Weather", response.ServiceName);
        Assert.Equal("GetForecast", response.OperationName);
        Assert.Equal("forecast for Zurich", response.Output);
    }

    [Fact]
    public async Task DispatchAsyncRunsMiddlewareBeforeHandler()
    {
        var middleware = new RecordingMiddleware();
        var dispatcher = new SmithyServerDispatcher([middleware]);
        dispatcher.Register(
            "Weather",
            "GetForecast",
            (request, _) =>
            {
                Assert.True(middleware.WasCalled);
                return Task.FromResult(
                    new SmithyServerResponse(
                        request.ServiceName,
                        request.OperationName,
                        request.Input
                    )
                );
            }
        );

        await dispatcher.DispatchAsync(new SmithyServerRequest("Weather", "GetForecast", "Zurich"));

        Assert.True(middleware.WasCalled);
    }

    [Fact]
    public async Task DispatchAsyncThrowsForUnknownOperation()
    {
        var dispatcher = new SmithyServerDispatcher();

        var error = await Assert.ThrowsAsync<SmithyServerOperationNotFoundException>(() =>
            dispatcher.DispatchAsync(new SmithyServerRequest("Weather", "GetForecast", "Zurich"))
        );

        Assert.Equal("Weather", error.ServiceName);
        Assert.Equal("GetForecast", error.OperationName);
    }

    private sealed class RecordingMiddleware : ISmithyServerMiddleware
    {
        public bool WasCalled { get; private set; }

        public Task<SmithyServerResponse> InvokeAsync(
            SmithyServerRequest request,
            SmithyServerOperationNext nextOperation,
            CancellationToken cancellationToken = default
        )
        {
            WasCalled = true;
            return nextOperation(request, cancellationToken);
        }
    }
}

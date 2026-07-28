using System.Net;
using System.Text;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class SmithyEndpointResolutionTests
{
    [Fact]
    public async Task ResolverRoutesRequestsPerOperation()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://static.example.com"),
            endpointResolver: new DelegateResolver(parameters => new SmithyEndpoint(
                new Uri($"https://{parameters.OperationId.Name.ToLowerInvariant()}.example.com")
            ))
        );

        await runtime.InvokeAsync(Binding(), "input");

        Assert.Equal("https://getforecast.example.com/input", transport.Request!.RequestUri);
    }

    [Fact]
    public async Task ResolverSeesOperationIdentifiersConfiguredEndpointAndInput()
    {
        SmithyEndpointParameters? seen = null;
        var configured = new Uri("https://static.example.com");
        var runtime = new SmithyClientRuntime(
            new RecordingTransport(),
            endpoint: configured,
            endpointResolver: new DelegateResolver(parameters =>
            {
                seen = parameters;
                return new SmithyEndpoint(configured);
            })
        );

        await runtime.InvokeAsync(Binding(), "input");

        Assert.NotNull(seen);
        Assert.Equal(ShapeId.Parse("example.weather#Weather"), seen!.ServiceId);
        Assert.Equal(ShapeId.Parse("example.weather#GetForecast"), seen.OperationId);
        Assert.Equal(configured, seen.ConfiguredEndpoint);
        Assert.Equal("input", seen.Input);
    }

    [Fact]
    public async Task ResolvedEndpointHeadersAreAppliedToEveryRequest()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpointResolver: new DelegateResolver(_ => new SmithyEndpoint(
                new Uri("https://api.example.com"),
                Headers: new Dictionary<string, string> { ["x-endpoint-key"] = "route-1" }
            ))
        );

        await runtime.InvokeAsync(Binding(), "input");

        Assert.Equal(["route-1"], transport.Request!.Headers["x-endpoint-key"]);
    }

    [Fact]
    public async Task ResolvedEndpointIsExposedInContext()
    {
        List<Uri> endpoints = [];
        var runtime = new SmithyClientRuntime(
            new RecordingTransport(),
            [new EndpointRecordingInterceptor(endpoints)],
            endpoint: new Uri("https://static.example.com"),
            endpointResolver: new DelegateResolver(_ => new SmithyEndpoint(
                new Uri("https://resolved.example.com")
            ))
        );

        await runtime.InvokeAsync(Binding(), "input");

        Assert.Equal([new Uri("https://resolved.example.com")], endpoints);
    }

    [Fact]
    public async Task EndpointAuthNarrowingSelectsTheNarrowedScheme()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpointResolver: new DelegateResolver(_ => new SmithyEndpoint(
                new Uri("https://api.example.com"),
                AuthSchemes: ["scheme#b"]
            )),
            authSchemes: new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingInterceptor("x-auth", "a"),
                ["scheme#b"] = new HeaderStampingInterceptor("x-auth", "b"),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: ["scheme#a", "scheme#b"]), "input");

        Assert.Equal(["b"], transport.Request!.Headers["x-auth"]);
    }

    [Fact]
    public async Task OperationAuthSchemesSelectTheConfiguredInterceptor()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com"),
            authSchemes: new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingInterceptor("x-auth", "a"),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: ["scheme#a"]), "input");

        Assert.Equal(["a"], transport.Request!.Headers["x-auth"]);
    }

    [Fact]
    public async Task AnonymousOperationsSendNoAuth()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com"),
            authSchemes: new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingInterceptor("x-auth", "a"),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: []), "input");

        Assert.False(transport.Request!.Headers.ContainsKey("x-auth"));
    }

    [Fact]
    public async Task AuthRunsAfterUserInterceptorsInEachPhase()
    {
        List<string> order = [];
        var runtime = new SmithyClientRuntime(
            new RecordingTransport(),
            [new PhaseRecordingInterceptor("user", order)],
            endpoint: new Uri("https://api.example.com"),
            authSchemes: new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new PhaseRecordingInterceptor("auth", order),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: ["scheme#a"]), "input");

        Assert.Equal(["user:signing", "auth:signing", "user:transmit", "auth:transmit"], order);
    }

    private static SmithyOperationBinding<string, string> Binding(
        IReadOnlyList<string>? authSchemeIds = null
    ) =>
        new(
            ShapeId.Parse("example.weather#Weather"),
            ShapeId.Parse("example.weather#GetForecast"),
            new TextProtocol(),
            authSchemeIds
        );

    private sealed class DelegateResolver(Func<SmithyEndpointParameters, SmithyEndpoint> resolve)
        : IEndpointResolver
    {
        public ValueTask<SmithyEndpoint> ResolveEndpointAsync(
            SmithyEndpointParameters parameters,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(resolve(parameters));
    }

    private sealed class RecordingTransport : IHttpTransport
    {
        public SmithyHttpRequest? Request { get; private set; }

        public Task<SmithyHttpClientResponse> SendAsync(
            SmithyHttpRequest request,
            SmithyHttpClientResponseMode responseMode,
            CancellationToken cancellationToken = default
        )
        {
            Request = request;
            return Task.FromResult(
                new SmithyHttpClientResponse(
                    HttpStatusCode.OK,
                    "OK",
                    Encoding.UTF8.GetBytes("serialized output"),
                    EmptyHeaders,
                    EmptyHeaders
                )
            );
        }
    }

    private sealed class EndpointRecordingInterceptor(List<Uri> endpoints) : IClientInterceptor
    {
        public void OnBeforeExecution(SmithyContext context)
        {
            endpoints.Add(context.Get(SmithyContextKeys.Endpoint));
        }
    }

    private sealed class HeaderStampingInterceptor(string name, string value) : IClientInterceptor
    {
        public ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            request.Headers[name] = [value];
            return ValueTask.FromResult(request);
        }
    }

    private sealed class PhaseRecordingInterceptor(string tag, List<string> order)
        : IClientInterceptor
    {
        public ValueTask<SmithyHttpRequest> OnBeforeSigningAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            order.Add($"{tag}:signing");
            return ValueTask.FromResult(request);
        }

        public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            CancellationToken cancellationToken = default
        )
        {
            order.Add($"{tag}:transmit");
            return ValueTask.FromResult(request);
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyHeaders { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private sealed class TextProtocol : IClientOperationProtocol<string, string>
    {
        public SmithyHttpRequest SerializeRequest(string input) =>
            new(HttpMethod.Post, $"/{input}");

        public string DeserializeResponse(SmithyHttpClientResponse response) => "output";

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(null);
    }
}

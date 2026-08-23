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
    public async Task ModeledHostPrefixExpandsAgainstResolvedEndpoint()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com/base")
        );

        await runtime.InvokeAsync(
            Binding(hostPrefix: input =>
                SmithyHostPrefix.Expand(
                    "{account}.data.",
                    new SmithyHostLabel("account", input)
                )
            ),
            "tenant"
        );

        Assert.Equal("https://tenant.data.api.example.com/base/tenant", transport.Request!.RequestUri);
    }

    [Fact]
    public async Task HostPrefixInjectionCanBeDisabled()
    {
        var transport = new RecordingTransport();
        var runtime = new SmithyClientRuntime(
            transport,
            endpoint: new Uri("https://api.example.com"),
            disableHostPrefixInjection: true
        );

        await runtime.InvokeAsync(
            Binding(hostPrefix: input =>
                SmithyHostPrefix.Expand("{account}.", new SmithyHostLabel("account", input))
            ),
            "tenant"
        );

        Assert.Equal("https://api.example.com/tenant", transport.Request!.RequestUri);
    }

    [Fact]
    public async Task DefaultUserAgentDoesNotOverwriteModeledHeader()
    {
        var defaultTransport = new RecordingTransport();
        await new SmithyClientRuntime(defaultTransport, endpoint: new Uri("https://api.example.com"))
            .InvokeAsync(Binding(), "input");

        Assert.StartsWith(
            "NSmithy.Client/",
            Assert.Single(defaultTransport.Request!.Headers["User-Agent"]),
            StringComparison.Ordinal
        );

        var modeledTransport = new RecordingTransport();
        await new SmithyClientRuntime(
            modeledTransport,
            endpoint: new Uri("https://api.example.com"),
            userAgent: "configured/2.0"
        ).InvokeAsync(Binding(modeledUserAgent: "modeled/1.0"), "input");

        Assert.Equal(["modeled/1.0"], modeledTransport.Request!.Headers["User-Agent"]);
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
    public async Task EndpointResolutionFailuresRunCompletionInterceptors()
    {
        List<Exception?> observed = [];
        var runtime = new SmithyClientRuntime(
            new RecordingTransport(),
            [new CompletionRecordingInterceptor(observed)],
            endpointResolver: new DelegateResolver(_ =>
                throw new InvalidOperationException("resolution failed")
            )
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(Binding(), "input")
        );

        Assert.Same(error, Assert.Single(observed));
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
    public async Task TypedContextCarriesOperationEndpointAndSelectedAuth()
    {
        ContextSnapshot? snapshot = null;
        var runtime = new SmithyClientRuntime(
            new RecordingTransport(),
            [new ContextRecordingInterceptor(value => snapshot = value)],
            endpoint: new Uri("https://api.example.com"),
            authSchemes: new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingAuthScheme("scheme#a", "x-auth", "a"),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: ["scheme#a"]), "input");

        Assert.NotNull(snapshot);
        Assert.Equal(ShapeId.Parse("example.weather#Weather"), snapshot!.ServiceId);
        Assert.Equal(ShapeId.Parse("example.weather#GetForecast"), snapshot.OperationId);
        Assert.Equal(new Uri("https://api.example.com"), snapshot.Endpoint.Uri);
        Assert.Equal("scheme#a", snapshot.AuthSchemeId);
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
            authSchemes: new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingAuthScheme("scheme#a", "x-auth", "a"),
                ["scheme#b"] = new HeaderStampingAuthScheme("scheme#b", "x-auth", "b"),
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
            authSchemes: new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingAuthScheme("scheme#a", "x-auth", "a"),
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
            authSchemes: new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new HeaderStampingAuthScheme("scheme#a", "x-auth", "a"),
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
            authSchemes: new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal)
            {
                ["scheme#a"] = new PhaseRecordingAuthScheme("scheme#a", "auth", order),
            }
        );

        await runtime.InvokeAsync(Binding(authSchemeIds: ["scheme#a"]), "input");

        Assert.Equal(["user:signing", "auth:signing", "user:transmit"], order);
    }

    private static SmithyOperationBinding<string, string> Binding(
        IReadOnlyList<string>? authSchemeIds = null,
        Func<string, string>? hostPrefix = null,
        string? modeledUserAgent = null
    ) =>
        new(
            ShapeId.Parse("example.weather#Weather"),
            ShapeId.Parse("example.weather#GetForecast"),
            new TextProtocol(modeledUserAgent),
            authSchemeIds,
            hostPrefix
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

    private sealed class CompletionRecordingInterceptor(List<Exception?> observed)
        : IClientInterceptor
    {
        public void OnAfterExecution(SmithyContext context, Exception? exception) =>
            observed.Add(exception);
    }

    private sealed record ContextSnapshot(
        ShapeId ServiceId,
        ShapeId OperationId,
        SmithyEndpoint Endpoint,
        string AuthSchemeId
    );

    private sealed class ContextRecordingInterceptor(Action<ContextSnapshot> record)
        : IClientInterceptor
    {
        public void OnBeforeExecution(SmithyContext context) =>
            record(
                new ContextSnapshot(
                    context.Get(SmithyContextKeys.ServiceId),
                    context.Get(SmithyContextKeys.OperationId),
                    context.Get(SmithyContextKeys.ResolvedEndpoint),
                    context.Get(SmithyContextKeys.AuthSchemeId)
                )
            );
    }

    private sealed class HeaderStampingAuthScheme(string schemeId, string name, string value)
        : ISmithyAuthScheme,
            ISmithySigner
    {
        public string SchemeId => schemeId;

        public ISmithyIdentityResolver IdentityResolver { get; } =
            new StaticSmithyIdentityResolver(new FakeIdentity());

        public ISmithySigner Signer => this;

        public ValueTask<SmithyHttpRequest> SignAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            ISmithyIdentity identity,
            CancellationToken cancellationToken = default
        )
        {
            request.Headers[name] = [value];
            return ValueTask.FromResult(request);
        }
    }

    private sealed class PhaseRecordingAuthScheme(string schemeId, string tag, List<string> order)
        : ISmithyAuthScheme,
            ISmithySigner
    {
        public string SchemeId => schemeId;

        public ISmithyIdentityResolver IdentityResolver { get; } =
            new StaticSmithyIdentityResolver(new FakeIdentity());

        public ISmithySigner Signer => this;

        public ValueTask<SmithyHttpRequest> SignAsync(
            SmithyContext context,
            SmithyHttpRequest request,
            ISmithyIdentity identity,
            CancellationToken cancellationToken = default
        )
        {
            order.Add($"{tag}:signing");
            return ValueTask.FromResult(request);
        }
    }

    private sealed class FakeIdentity : ISmithyIdentity;

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

    private sealed class TextProtocol(string? userAgent = null)
        : IClientOperationProtocol<string, string>
    {
        public SmithyHttpRequest SerializeRequest(
            string input,
            CancellationToken cancellationToken = default
        )
        {
            var request = new SmithyHttpRequest(HttpMethod.Post, $"/{input}");
            if (userAgent is not null)
            {
                request.Headers["User-Agent"] = [userAgent];
            }

            return request;
        }

        public ValueTask<string> DeserializeResponseAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult("output");

        public bool IsErrorResponse(SmithyHttpClientResponse response) =>
            (int)response.StatusCode >= 400;

        public ValueTask<Exception?> DeserializeErrorAsync(
            SmithyHttpClientResponse response,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<Exception?>(null);
    }
}

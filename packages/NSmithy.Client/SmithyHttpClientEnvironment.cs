using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Client;

/// <summary>
/// Constructs the HTTP execution environment shared by generated clients. Disposing the
/// environment releases only the HttpClient it created; supplied clients and runtimes remain
/// caller-owned. Operation protocols are bound by generated code using ServiceProtocol.
/// </summary>
public sealed class SmithyHttpClientEnvironment : IDisposable
{
    private readonly HttpClient? ownedHttpClient;

    private SmithyHttpClientEnvironment(
        SmithyClientRuntime runtime,
        IServiceProtocol serviceProtocol,
        HttpClient? ownedHttpClient = null
    )
    {
        Runtime = runtime;
        ServiceProtocol = serviceProtocol;
        this.ownedHttpClient = ownedHttpClient;
    }

    public SmithyClientRuntime Runtime { get; }

    public IServiceProtocol ServiceProtocol { get; }

    /// <summary>
    /// Creates a runtime using common configuration and generated service defaults. A supplied
    /// HttpClient retains its version settings; modeled preferences apply only to an owned client
    /// using the default protocol. The caller's configuration is never modified.
    /// </summary>
    /// <param name="modeledEndpointResolver">
    /// Creates the service's generated endpoint resolver from the configured endpoint (null when
    /// none is set). Used unless <c>Config.EndpointResolver</c> is set; a service without endpoint
    /// rules passes null and requires either a configured endpoint or a custom resolver.
    /// </param>
    public static SmithyHttpClientEnvironment Create(
        ServiceSchema service,
        SmithyClientConfig config,
        Func<IProtocol> defaultProtocol,
        IReadOnlyList<string> modeledAuthSchemes,
        SmithyHttpVersionPreference? modeledHttpVersion = null,
        HttpClient? httpClient = null,
        Func<Uri?, IEndpointResolver>? modeledEndpointResolver = null
    )
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(defaultProtocol);
        ArgumentNullException.ThrowIfNull(modeledAuthSchemes);

        var endpoint = config.Endpoint ?? httpClient?.BaseAddress;
        var endpointResolver = config.EndpointResolver ?? modeledEndpointResolver?.Invoke(endpoint);
        if (endpoint is null && endpointResolver is null)
        {
            throw new ArgumentException(
                "Set Config.Endpoint, httpClient.BaseAddress, or Config.EndpointResolver, or provide modeled endpoint rules.",
                nameof(config)
            );
        }
        var protocol = config.Protocol ?? defaultProtocol();
        var serviceProtocol = protocol.ForService(service);
        var authSchemes = SmithyAuthSchemeResolver.ResolveSchemes(
            service,
            modeledAuthSchemes,
            config.AuthSchemes
        );
        HttpClient? ownedClient = null;
        try
        {
            if (httpClient is null)
            {
                ownedClient = new HttpClient();
                ConfigureHttpClient(ownedClient, config, () => protocol, modeledHttpVersion);
                httpClient = ownedClient;
            }

            var runtime = new SmithyClientRuntime(
                new HttpClientTransport(httpClient),
                config.Interceptors,
                config.RetryStrategy,
                endpoint,
                config.OperationTimeout,
                endpointResolver,
                authSchemes,
                config.DisableHostPrefixInjection,
                config.UserAgent
            );
            return new SmithyHttpClientEnvironment(runtime, serviceProtocol, ownedClient);
        }
        catch
        {
            ownedClient?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Applies protocol defaults to an owned or factory-created HttpClient. Factory registrations
    /// call this before application configuration so the application's overrides take precedence.
    /// Do not call this on a caller-supplied client whose settings should be preserved.
    /// </summary>
    public static void ConfigureHttpClient(
        HttpClient client,
        SmithyClientConfig config,
        Func<IProtocol> defaultProtocol,
        SmithyHttpVersionPreference? modeledHttpVersion = null
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(defaultProtocol);
        var preference = config.Protocol is { } protocol
            ? protocol.HttpVersionPreference
            : modeledHttpVersion ?? defaultProtocol().HttpVersionPreference;
        preference.Apply(client);
    }

    /// <summary>
    /// Binds the service to a caller-owned runtime. Only Protocol is read from config;
    /// the supplied runtime already defines transport, authentication, retries and interceptors.
    /// </summary>
    public static SmithyHttpClientEnvironment FromRuntime(
        ServiceSchema service,
        SmithyClientRuntime runtime,
        SmithyClientConfig config,
        Func<IProtocol> defaultProtocol
    )
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(defaultProtocol);
        return new SmithyHttpClientEnvironment(
            runtime,
            (config.Protocol ?? defaultProtocol()).ForService(service)
        );
    }

    public void Dispose() => ownedHttpClient?.Dispose();
}

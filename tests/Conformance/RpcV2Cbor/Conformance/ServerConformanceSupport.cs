using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace RpcV2Cbor.Conformance;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852:Seal internal types")]
internal class RpcV2CborServerDispatchProxy : DispatchProxy
{
    public required Func<MethodInfo, object?[]?, object?> Invoker { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return Invoker(targetMethod, args);
    }
}

internal sealed class RpcV2CborServerHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private RpcV2CborServerHost(WebApplication app, HttpClient client)
    {
        this.app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<RpcV2CborServerHost> StartAsync(
        string operationName,
        Func<MethodInfo, object?[]?, object?> invoker,
        CancellationToken cancellationToken = default
    )
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var operationHandler = ResolveOperationHandlerInterface(operationName);
        var aggregateHandler = ResolveAggregateHandlerInterface(operationHandler);
        var mapMethod = ResolveMapMethod(aggregateHandler);
        var handler = CreateProxy(aggregateHandler, invoker);

        builder.Services.AddSingleton(aggregateHandler, handler);
        foreach (var contract in aggregateHandler.GetInterfaces())
        {
            builder.Services.AddSingleton(contract, _ => handler);
        }

        var app = builder.Build();
        mapMethod.Invoke(null, [app]);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address =
            app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal))
            ?? app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Unable to determine the RpcV2Cbor test server address."
            );

        return new RpcV2CborServerHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync().ConfigureAwait(false);
    }

    private static Type ResolveOperationHandlerInterface(string operationName)
    {
        var assembly = typeof(RpcV2CborServerHost).Assembly;
        var handlerName = "I" + operationName + "Handler";
        return assembly
            .GetTypes()
            .Single(t =>
                t.IsInterface && string.Equals(t.Name, handlerName, StringComparison.Ordinal)
            );
    }

    private static Type ResolveAggregateHandlerInterface(Type operationHandler)
    {
        return operationHandler
            .Assembly.GetTypes()
            .Single(t =>
                t.IsInterface
                && t != operationHandler
                && t.Name.EndsWith("ServiceHandler", StringComparison.Ordinal)
                && operationHandler.IsAssignableFrom(t)
            );
    }

    private static MethodInfo ResolveMapMethod(Type aggregateHandler)
    {
        var serviceName = aggregateHandler.Name["I".Length..^"Handler".Length];
        return aggregateHandler
            .Assembly.GetTypes()
            .Where(t => t.IsSealed && t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(m =>
            {
                var parameters = m.GetParameters();
                return m.Name == $"Map{serviceName}Http"
                    && parameters.Length == 1
                    && parameters[0].ParameterType.FullName
                        == "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
            });
    }

    private static object CreateProxy(
        Type aggregateHandler,
        Func<MethodInfo, object?[]?, object?> invoker
    )
    {
        var create = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(DispatchProxy.Create) && m.IsGenericMethodDefinition)
            .MakeGenericMethod(aggregateHandler, typeof(RpcV2CborServerDispatchProxy));
        var proxy =
            create.Invoke(null, null)
            ?? throw new InvalidOperationException(
                $"Unable to create proxy for {aggregateHandler}."
            );
        ((RpcV2CborServerDispatchProxy)proxy).Invoker = invoker;
        return proxy;
    }
}

internal static class ServerCborRequestFactory
{
    private const string CborContentType = "application/cbor";

    /// <summary>
    /// Builds an HTTP request from a protocol test case for a rpcv2Cbor operation.
    /// The body in the test fixture is hex-encoded CBOR binary.
    /// </summary>
    public static HttpRequestMessage FromTestCase(HttpRequestTestCase testCase, Uri baseAddress)
    {
        var uri = BuildUri(baseAddress, testCase.Uri, testCase.QueryParams);
        var request = new HttpRequestMessage(new HttpMethod(testCase.Method), uri);

        // rpcv2Cbor bodies are base64-encoded binary in the test fixture
        byte[]? bodyBytes = null;
        if (testCase.Body is not null || testCase.BodyMediaType is not null)
        {
            bodyBytes = string.IsNullOrEmpty(testCase.Body)
                ? []
                : Convert.FromBase64String(testCase.Body);
        }

        ByteArrayContent? content = null;
        if (bodyBytes is not null)
        {
            content = new ByteArrayContent(bodyBytes);
            request.Content = content;
        }

        foreach (var (name, value) in testCase.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
                continue;

            if (
                string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
            )
            {
                content ??= new ByteArrayContent([]);
                request.Content ??= content;
                request.Content.Headers.TryAddWithoutValidation(name, value);
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (testCase.BodyMediaType is not null && content is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", testCase.BodyMediaType);
        }

        return request;
    }

    private static Uri BuildUri(
        Uri baseAddress,
        string pathAndMaybeQuery,
        IReadOnlyList<string> queryParams
    )
    {
        var path = pathAndMaybeQuery;
        var query = "";
        var queryIndex = pathAndMaybeQuery.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = pathAndMaybeQuery[..queryIndex];
            query = pathAndMaybeQuery[(queryIndex + 1)..];
        }

        if (queryParams.Count > 0)
        {
            var combined = string.Join("&", queryParams);
            query = string.IsNullOrEmpty(query) ? combined : $"{query}&{combined}";
        }

        var builder = new UriBuilder(new Uri(baseAddress, path));
        builder.Query = query;
        return builder.Uri;
    }

}

internal static class ServerCborResponseAssertions
{
    public static async Task AssertAsync(HttpResponseTestCase expected, HttpResponseMessage actual)
    {
        Assert.Equal(expected.Code, (int)actual.StatusCode);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in actual.Headers)
            headers[header.Key] = string.Join(",", header.Value);
        foreach (var header in actual.Content.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        foreach (var (name, value) in expected.Headers)
        {
            Assert.True(headers.TryGetValue(name, out var actualValue), $"Missing header '{name}'.");
            Assert.Equal(value, actualValue);
        }

        var actualBodyBytes = await actual.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(expected.Body))
        {
            Assert.Empty(actualBodyBytes);
            return;
        }

        // rpcv2Cbor bodies are base64-encoded binary; use structural comparison to
        // tolerate definite/indefinite length differences between the fixture and codec.
        var expectedBytes = Convert.FromBase64String(expected.Body);
        CborAssert.AreStructurallyEqual(expectedBytes, actualBodyBytes);
    }

}

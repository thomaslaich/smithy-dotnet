using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aws.Protocoltests.Restjson;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace RestJson1.Conformance;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852:Seal internal types")]
internal class RestJsonServerDispatchProxy : DispatchProxy
{
    public required Func<MethodInfo, object?[]?, object?> Invoker { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return Invoker(targetMethod, args);
    }
}

internal sealed class RestJsonServerHost : IAsyncDisposable
{
    private readonly WebApplication app;

    private RestJsonServerHost(WebApplication app, HttpClient client)
    {
        this.app = app;
        Client = client;
    }

    public HttpClient Client { get; }

    public static async Task<RestJsonServerHost> StartAsync(
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
        // Surface server-side exceptions as a 500 with the message in the body so conformance
        // failures report the actual cause instead of an opaque status code.
        app.Use(
            async (ctx, next) =>
            {
                try
                {
                    await next().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsync(ex.ToString()).ConfigureAwait(false);
                }
            }
        );
        mapMethod.Invoke(null, [app]);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var address =
            app.Urls.FirstOrDefault(url => url.StartsWith("http://", StringComparison.Ordinal))
            ?? app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()
                ?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Unable to determine the RestJson test server address."
            );

        return new RestJsonServerHost(app, new HttpClient { BaseAddress = new Uri(address) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync().ConfigureAwait(false);
    }

    private static Type ResolveOperationHandlerInterface(string operationName)
    {
        var assembly = typeof(RestJsonServerHost).Assembly;
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
            .MakeGenericMethod(aggregateHandler, typeof(RestJsonServerDispatchProxy));
        var proxy =
            create.Invoke(null, null)
            ?? throw new InvalidOperationException(
                $"Unable to create proxy for {aggregateHandler}."
            );
        ((RestJsonServerDispatchProxy)proxy).Invoker = invoker;
        return proxy;
    }
}

/// <summary>
/// Identifies which operations the generated server actually exposes. The protocol-test bundle
/// carries fixtures for several auxiliary AWS services (Glacier, API Gateway, …) whose operations
/// are not part of the generated <c>RestJson</c> service, so the server suite only drives cases for
/// operations that have a generated <c>I{Operation}Handler</c>.
/// </summary>
internal static class GeneratedService
{
    private static readonly HashSet<string> HandlerOperations = typeof(RestJsonServerHost)
        .Assembly.GetTypes()
        .Where(t =>
            t.IsInterface
            && t.Name.StartsWith('I')
            && t.Name.EndsWith("Handler", StringComparison.Ordinal)
            && !t.Name.EndsWith("ServiceHandler", StringComparison.Ordinal)
        )
        .Select(t => t.Name["I".Length..^"Handler".Length])
        .ToHashSet(StringComparer.Ordinal);

    public static bool HasHandler(string operationName) =>
        HandlerOperations.Contains(operationName);
}

internal static class ConformanceObjectFactory
{
    public static object? BuildDefault(Type type) => BuildDefault(type, depth: 0);

    private static object? BuildDefault(Type type, int depth)
    {
        if (depth > 6)
            return null;
        if (type == typeof(string))
            return string.Empty;
        if (type.IsValueType)
        {
            var schemaKind = GetSchemaKind(type);
            if (schemaKind == ShapeKind.Enum)
            {
                var staticInst = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(p => p.PropertyType == type)
                    ?.GetValue(null);
                if (staticInst is not null)
                    return staticInst;
            }
            return Activator.CreateInstance(type);
        }
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (
                def == typeof(IEnumerable<>)
                || def == typeof(IReadOnlyList<>)
                || def == typeof(IList<>)
                || def == typeof(ICollection<>)
                || def == typeof(IReadOnlyCollection<>)
                || def == typeof(List<>)
            )
            {
                var elem = type.GetGenericArguments()[0];
                return Array.CreateInstance(elem, 0);
            }
            if (
                def == typeof(IDictionary<,>)
                || def == typeof(IReadOnlyDictionary<,>)
                || def == typeof(Dictionary<,>)
            )
            {
                return Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments())
                );
            }
        }
        if (type.IsAbstract)
        {
            var concrete = type
                .Assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && type.IsAssignableFrom(t));
            return concrete is null ? null : BuildDefault(concrete, depth + 1);
        }

        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
            return null;

        var args = ctor.GetParameters()
            .Select(p =>
                p.HasDefaultValue ? p.DefaultValue : BuildDefault(p.ParameterType, depth + 1)
            )
            .ToArray();
        return ctor.Invoke(args);
    }

    private static ShapeKind? GetSchemaKind(Type type)
    {
        // The functional schema lives on the generated companion `{Type}Schema` class.
        var schemaType = type.Assembly.GetType(type.FullName + "Schema");
        var schemaProp = schemaType?.GetProperty(
            "Schema",
            BindingFlags.Public | BindingFlags.Static
        );
        return (schemaProp?.GetValue(null) as Schema)?.Kind;
    }
}

internal static class ServerConformanceRequestFactory
{
    public static HttpRequestMessage FromTestCase(HttpRequestTestCase testCase, Uri baseAddress)
    {
        var uri = BuildUri(baseAddress, testCase.Uri, testCase.QueryParams);
        var request = new HttpRequestMessage(new HttpMethod(testCase.Method), uri);

        ByteArrayContent? content = null;
        if (testCase.Body is not null || testCase.BodyMediaType is not null)
        {
            content = new ByteArrayContent(
                testCase.Body is null ? [] : Encoding.UTF8.GetBytes(testCase.Body)
            );
            request.Content = content;
        }

        foreach (var (name, value) in testCase.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

    public static HttpRequestMessage ForOperation(string operationId, JsonObject shapes)
    {
        var operation = (JsonObject)shapes[operationId]!;
        var http = (JsonObject)((JsonObject)operation["traits"]!)["smithy.api#http"]!;
        var method = (string)http["method"]!;
        var rawUri = (string)http["uri"]!;
        var requestUri = rawUri.Length > 0 && rawUri[0] == '/' ? rawUri : "/" + rawUri;

        var queryIndex = requestUri.IndexOf('?');
        var path = queryIndex >= 0 ? requestUri[..queryIndex] : requestUri;
        var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (queryIndex >= 0)
        {
            request.RequestUri = new Uri(path + requestUri[queryIndex..], UriKind.Relative);
        }

        if (ShouldSendJsonBody(operation, shapes))
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
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

    private static bool ShouldSendJsonBody(JsonObject operation, JsonObject shapes)
    {
        if (operation["input"] is not JsonObject inputRef)
        {
            return false;
        }

        var input = shapes[(string)inputRef["target"]!] as JsonObject;
        var members = input?["members"] as JsonObject;
        if (members is null || members.Count == 0)
        {
            return false;
        }

        foreach (var (_, memberNode) in members)
        {
            var member = memberNode as JsonObject;
            var traits = member?["traits"] as JsonObject;
            if (traits is null)
            {
                return true;
            }

            if (
                !traits.ContainsKey("smithy.api#httpLabel")
                && !traits.ContainsKey("smithy.api#httpQuery")
                && !traits.ContainsKey("smithy.api#httpQueryParams")
                && !traits.ContainsKey("smithy.api#httpHeader")
                && !traits.ContainsKey("smithy.api#httpPrefixHeaders")
                && !traits.ContainsKey("smithy.api#httpPayload")
            )
            {
                return true;
            }
        }

        return false;
    }
}

internal static class ServerResponseAssertions
{
    public static async Task AssertAsync(HttpResponseTestCase expected, HttpResponseMessage actual)
    {
        Assert.Equal(expected.Code, (int)actual.StatusCode);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in actual.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }
        foreach (var header in actual.Content.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        foreach (var (name, value) in expected.Headers)
        {
            Assert.True(
                headers.TryGetValue(name, out var actualValue),
                $"Missing header '{name}'."
            );
            Assert.Equal(value, actualValue);
        }

        // `bodyMediaType` describes how to compare the body, not a required Content-Type header.
        // Content-Type is asserted only when the case lists it in `headers` (handled above).
        var actualBody = actual.Content is null
            ? ""
            : await actual.Content.ReadAsStringAsync().ConfigureAwait(false);
        AssertBody(expected.Body ?? "", actualBody);
    }

    private static void AssertBody(string expectedBody, string actualBody)
    {
        if (IsEmptyJsonEquivalent(expectedBody, actualBody))
            return;

        if (TryParseJson(expectedBody, out var ej) && TryParseJson(actualBody, out var aj))
        {
            Assert.True(
                JsonEquals(ej, aj),
                $"JSON body mismatch.\nExpected: {expectedBody}\nActual:   {actualBody}"
            );
            return;
        }

        Assert.Equal(expectedBody, actualBody);
    }

    private static bool TryParseJson(string s, out JsonNode? node)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            node = null;
            return true;
        }

        try
        {
            node = JsonNode.Parse(s);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static bool JsonEquals(JsonNode? a, JsonNode? b) =>
        (a, b) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (JsonObject ao, JsonObject bo) => ao.All(kv =>
                bo.TryGetPropertyValue(kv.Key, out var bv) && JsonEquals(kv.Value, bv)
            ) && bo.All(kv => ao.ContainsKey(kv.Key) || kv.Value is null),
            (JsonArray aa, JsonArray bb) => aa.Count == bb.Count
                && aa.Zip(bb, JsonEquals).All(x => x),
            (JsonValue av, JsonValue bv) => JsonValueEquals(av, bv),
            _ => false,
        };

    private static bool JsonValueEquals(JsonValue a, JsonValue b)
    {
        if (a.TryGetValue<double>(out var ad) && b.TryGetValue<double>(out var bd))
            return ad == bd;
        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }

    private static bool IsEmptyJsonEquivalent(string expectedBody, string actualBody) =>
        (string.IsNullOrWhiteSpace(expectedBody) && actualBody == "{}")
        || (string.IsNullOrWhiteSpace(actualBody) && expectedBody == "{}");
}

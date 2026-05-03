using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSmithy.Client;

namespace RpcV2Cbor.Conformance;

/// <summary>
/// Drives a single httpRequestTests case end-to-end:
///   1. resolves the operation's input type from the generated client surface;
///   2. binds the case's {@code params} into an instance via {@link ParamBinder};
///   3. invokes the operation through a {@link RecordingHttpMessageHandler};
///   4. asserts the captured wire request matches the expected method/uri/headers/body.
/// </summary>
internal static class HttpRequestRunner
{
    private static readonly Uri Endpoint = new("http://localhost");

    /// <summary>
    /// Generated client types in the test assembly. We discover these once via reflection so the
    /// runner can dispatch to whichever service owns each protocol-test case.
    /// </summary>
    private static readonly IReadOnlyList<Type> ClientTypes =
    [
        .. typeof(HttpRequestRunner)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && t.Name.EndsWith("Client", StringComparison.Ordinal)
                && t.GetConstructors()
                    .Any(c =>
                    {
                        var ps = c.GetParameters();
                        return ps.Length == 2 && ps[0].ParameterType == typeof(HttpClient);
                    })
            ),
    ];

    public static async Task RunAsync(HttpRequestTestCase testCase)
    {
        var operationName = testCase.OperationName + "Async";
        MethodInfo? method = null;
        Type? clientType = null;
        foreach (var t in ClientTypes)
        {
            method = t.GetMethod(operationName, BindingFlags.Public | BindingFlags.Instance);
            if (method is not null)
            {
                clientType = t;
                break;
            }
        }
        if (method is null || clientType is null)
        {
            throw new InvalidOperationException(
                $"Operation method {operationName} not found on any generated client ("
                    + string.Join(", ", ClientTypes.Select(t => t.FullName))
                    + ")."
            );
        }
        var inputType = method.GetParameters()[0].ParameterType;
        var input = ParamBinder.Bind(inputType, testCase.Params ?? new JsonObject())!;

        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        using var httpClient = new HttpClient(handler);
        var client = Activator.CreateInstance(
            clientType,
            httpClient,
            new SmithyClientOptions { Endpoint = Endpoint }
        )!;

        try
        {
            var task = (Task)method.Invoke(client, [input, CancellationToken.None])!;
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // The output deserializer often fails because EmptyOk() returns no body, but the
            // outgoing request was still captured before the response was processed. Only
            // re-throw if no request was captured (i.e. the call failed before sending).
            if (handler.Captured is null)
                throw tie.InnerException;
        }
        catch (Exception)
        {
            if (handler.Captured is null)
                throw;
        }

        var captured =
            handler.Captured
            ?? throw new InvalidOperationException("Client did not send any HTTP request.");
        RequestAssertions.Assert(testCase, captured);
    }
}

internal static class RequestAssertions
{
    public static void Assert(HttpRequestTestCase expected, RecordedRequest actual)
    {
        Xunit.Assert.Equal(expected.Method, actual.Method);

        var expectedPath = NormalizePath(PathOf(expected.Uri));
        var actualPath = NormalizePath(actual.RequestUri.AbsolutePath);
        Xunit.Assert.Equal(expectedPath, actualPath);

        // Query parameters: expected.QueryParams is a list of "k=v" strings (URL-encoded).
        // Compare as multisets (order doesn't matter per the smithy.test spec).
        var expectedQuery = NormalizeQueryParams(expected.QueryParams)
            .Concat(NormalizeQueryParams(QuerySplit(QueryOf(expected.Uri))))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var actualQuery = NormalizeQueryParams(QuerySplit(actual.RequestUri.Query))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Xunit.Assert.Equal(expectedQuery, actualQuery);

        foreach (var name in expected.RequireQueryParams)
        {
            Xunit.Assert.Contains(
                actualQuery,
                q => q.StartsWith(name + "=", StringComparison.Ordinal) || q == name
            );
        }
        foreach (var name in expected.ForbidQueryParams)
        {
            Xunit.Assert.DoesNotContain(
                actualQuery,
                q => q.StartsWith(name + "=", StringComparison.Ordinal) || q == name
            );
        }

        foreach (var (name, value) in expected.Headers)
        {
            if (!actual.Headers.TryGetValue(name, out var values))
            {
                Xunit.Assert.Fail(
                    $"Expected header '{name}' was not sent. "
                        + $"Sent headers: {string.Join(", ", actual.Headers.Keys)}"
                );
            }
            Xunit.Assert.Equal(value, string.Join(",", values));
        }
        foreach (var name in expected.RequireHeaders)
            Xunit.Assert.True(
                actual.Headers.ContainsKey(name),
                $"Header '{name}' was required but missing."
            );
        foreach (var name in expected.ForbidHeaders)
            Xunit.Assert.False(
                actual.Headers.ContainsKey(name),
                $"Header '{name}' was forbidden but present."
            );

        AssertBody(expected, actual);
    }

    private static void AssertBody(HttpRequestTestCase expected, RecordedRequest actual)
    {
        var expectedBody = expected.Body ?? "";
        var actualBody = Encoding.UTF8.GetString(actual.Body);
        // Always prefer structural JSON comparison when both sides parse as JSON; fall back to
        // exact string equality (covers raw text payloads).
        if (TryParseJson(expectedBody, out var ej) && TryParseJson(actualBody, out var aj))
        {
            Xunit.Assert.True(
                JsonEquals(ej, aj),
                $"JSON body mismatch.\nExpected: {expectedBody}\nActual:   {actualBody}"
            );
            return;
        }
        Xunit.Assert.Equal(expectedBody, actualBody);
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
            (JsonObject ao, JsonObject bo) => ao.Count == bo.Count
                && ao.All(kv =>
                    bo.TryGetPropertyValue(kv.Key, out var bv) && JsonEquals(kv.Value, bv)
                ),
            (JsonArray aa, JsonArray bb) => aa.Count == bb.Count
                && aa.Zip(bb, JsonEquals).All(x => x),
            (JsonValue av, JsonValue bv) => JsonValueEquals(av, bv),
            _ => false,
        };

    private static bool JsonValueEquals(JsonValue a, JsonValue b)
    {
        // Compare numbers numerically so 9 / 9.0 / 9.00 collapse, but keep strings/bools strict.
        if (a.TryGetValue<double>(out var ad) && b.TryGetValue<double>(out var bd))
            return ad == bd;
        return string.Equals(a.ToJsonString(), b.ToJsonString(), StringComparison.Ordinal);
    }

    private static string PathOf(string uri)
    {
        var qIdx = uri.IndexOf('?');
        return qIdx < 0 ? uri : uri[..qIdx];
    }

    private static string QueryOf(string uri)
    {
        var qIdx = uri.IndexOf('?');
        return qIdx < 0 ? "" : uri[(qIdx + 1)..];
    }

    private static string[] QuerySplit(string query) =>
        string.IsNullOrEmpty(query)
            ? []
            : query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> NormalizeQueryParams(IEnumerable<string> entries) =>
        entries.Select(NormalizeQueryParam);

    private static string NormalizeQueryParam(string entry)
    {
        // Smithy expected entries may use unencoded characters; URL decode both sides for
        // comparison.
        var eq = entry.IndexOf('=');
        if (eq < 0)
            return Uri.UnescapeDataString(entry);
        return Uri.UnescapeDataString(entry[..eq])
            + "="
            + Uri.UnescapeDataString(entry[(eq + 1)..]);
    }

    private static string NormalizePath(string path) => path.Length > 1 ? path.TrimEnd('/') : path;
}

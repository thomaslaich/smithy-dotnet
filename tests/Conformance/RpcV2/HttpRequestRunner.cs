using System.Reflection;
using System.Text.Json.Nodes;
using NSmithy.Client;

namespace RpcV2.Conformance;

/// <summary>
/// Drives a single httpRequestTests case end-to-end:
///   1. resolves the operation's input type from the generated client surface;
///   2. binds the case's {@code params} into an instance via {@link ParamBinder};
///   3. invokes the operation through a {@link RecordingHttpMessageHandler};
///   4. asserts the captured wire request matches the expected method/uri/headers/body.
/// </summary>
internal static class HttpRequestRunner
{
    /// <summary>
    /// The fixture's <c>host</c>, so a host prefix (<c>@endpoint</c>) lands where
    /// <c>resolvedHost</c> expects it.
    /// </summary>
    private static Uri ResolveEndpoint(HttpRequestTestCase testCase)
    {
        if (string.IsNullOrWhiteSpace(testCase.Host))
        {
            return new Uri("http://localhost");
        }

        return
            testCase.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || testCase.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(testCase.Host)
            : new Uri("http://" + testCase.Host);
    }

    public static async Task RunAsync(HttpRequestTestCase testCase)
    {
        var (clientType, method) = ConformanceClients.ResolveOperation(testCase.ShapeId);
        var inputType = method.GetParameters()[0].ParameterType;
        var input = ParamBinder.Bind(inputType, testCase.Params ?? new JsonObject())!;

        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        using var httpClient = new HttpClient(handler);
        var client = ConformanceClients.Build(clientType, httpClient, ResolveEndpoint(testCase));

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

        if (expected.ResolvedHost is not null)
            Xunit.Assert.Equal(expected.ResolvedHost, actual.RequestUri.Host);

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

        ConformanceProtocol.AssertBody(expected.Body, actual.Body);
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

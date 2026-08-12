using System.Text;
using System.Text.Json.Nodes;

namespace RestJson1.Conformance;

/// <summary>
/// Drives one malformed-request case against the generated server: send the forbidden request,
/// assert the server rejects it with the modeled status, error type, and body — and that the
/// handler was never reached, since a rejected request must not become a side effect.
/// </summary>
internal static class ServerHttpMalformedRequestRunner
{
    public static async Task RunAsync(HttpMalformedRequestTestCase testCase)
    {
        var handlerInvoked = false;

        await using var host = await RestJsonServerHost
            .StartAsync(
                testCase.OperationName,
                (method, args) =>
                {
                    handlerInvoked = true;
                    throw new InvalidOperationException(
                        $"Handler for {testCase.OperationName} ran for malformed case {testCase.Id}."
                    );
                }
            )
            .ConfigureAwait(false);

        using var request = BuildRequest(testCase, host.Client.BaseAddress!);
        using var response = await host.Client.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        Assert.False(handlerInvoked, $"Handler ran despite a malformed request ({testCase.Id}).");
        Assert.True(
            testCase.ExpectedCode == (int)response.StatusCode,
            $"Expected {testCase.ExpectedCode} but got {(int)response.StatusCode} for {testCase.Id}.\n{body}"
        );

        foreach (var (name, expected) in testCase.ExpectedHeaders)
        {
            Assert.True(
                TryGetHeader(response, name, out var actual),
                $"Response is missing header '{name}' for {testCase.Id}.\n{body}"
            );
            Assert.Equal(expected, actual);
        }

        AssertBody(testCase, body);
    }

    private static void AssertBody(HttpMalformedRequestTestCase testCase, string body)
    {
        if (testCase.ExpectedMessageRegex is { } regex)
        {
            Assert.Matches(regex, body);
            return;
        }

        if (testCase.ExpectedBodyContents is not { } expected)
        {
            return;
        }

        if (testCase.ExpectedBodyMediaType is "application/json")
        {
            // Compared as JSON: member order and whitespace are not part of the contract.
            Assert.Equal(
                Canonical(JsonNode.Parse(expected)),
                Canonical(JsonNode.Parse(body)),
                StringComparer.Ordinal
            );
            return;
        }

        Assert.Equal(expected, body);
    }

    private static string Canonical(JsonNode? node) =>
        node switch
        {
            JsonObject o => "{"
                + string.Join(
                    ",",
                    o.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => $"{p.Key}:{Canonical(p.Value)}")
                )
                + "}",
            JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
            null => "null",
            _ => node.ToJsonString(),
        };

    private static bool TryGetHeader(HttpResponseMessage response, string name, out string? value)
    {
        if (response.Headers.TryGetValues(name, out var headerValues))
        {
            value = string.Join(", ", headerValues);
            return true;
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            value = string.Join(", ", contentValues);
            return true;
        }

        value = null;
        return false;
    }

    private static HttpRequestMessage BuildRequest(
        HttpMalformedRequestTestCase testCase,
        Uri baseAddress
    )
    {
        var uri = testCase.Uri;
        if (testCase.QueryParams.Count > 0)
        {
            uri +=
                (uri.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                + string.Join("&", testCase.QueryParams);
        }

        var request = new HttpRequestMessage(
            new HttpMethod(testCase.Method),
            new Uri(baseAddress, uri)
        );

        // The body is sent exactly as written, including bodies that are not valid JSON — that is
        // the point of the suite, so nothing here may normalize it.
        HttpContent? content = testCase.Body is null
            ? null
            : new ByteArrayContent(Encoding.UTF8.GetBytes(testCase.Body));
        request.Content = content;

        foreach (var (name, value) in testCase.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                content ??= new ByteArrayContent([]);
                request.Content ??= content;
                request.Content.Headers.Remove(name);
                request.Content.Headers.TryAddWithoutValidation(name, value);
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }
}

using System.Reflection;
using System.Text.Json.Nodes;

namespace SimpleRestJson.Conformance;

internal static class ServerHttpRequestRunner
{
    public static async Task RunAsync(HttpRequestTestCase testCase)
    {
        object? capturedInput = null;
        MethodInfo? capturedMethod = null;

        await using var host = await RestJsonServerHost
            .StartAsync(
                testCase.OperationName,
                (method, args) =>
                {
                    capturedMethod = method;
                    capturedInput = args?.FirstOrDefault(arg => arg is not CancellationToken);
                    return CreateSuccessfulResult(method);
                }
            )
            .ConfigureAwait(false);

        using var request = ServerConformanceRequestFactory.FromTestCase(
            testCase,
            host.Client.BaseAddress!
        );
        using var response = await host.Client.SendAsync(request).ConfigureAwait(false);
        var responseBody =
            (int)response.StatusCode >= 500
                ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                : "";
        Assert.True(
            (int)response.StatusCode < 500,
            $"Server returned {(int)response.StatusCode} for {testCase.Id}.\n{responseBody}"
        );

        Assert.NotNull(capturedMethod);
        Assert.Equal(testCase.OperationName + "Async", capturedMethod!.Name);
        if (testCase.Params is not null)
        {
            Assert.NotNull(capturedInput);
            ResponseAssertions.AssertEquivalent(
                testCase.Params,
                capturedInput!,
                testCase.OperationName
            );
        }
    }

    private static object CreateSuccessfulResult(MethodInfo method)
    {
        if (method.ReturnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        var resultType = method.ReturnType.GetGenericArguments()[0];
        var result = ConformanceObjectFactory.BuildDefault(resultType);
        return typeof(Task)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Task.FromResult))
            .MakeGenericMethod(resultType)
            .Invoke(null, [result])!;
    }
}

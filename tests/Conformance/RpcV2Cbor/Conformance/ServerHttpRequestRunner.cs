using System.Reflection;
using System.Text.Json.Nodes;

namespace RpcV2Cbor.Conformance;

internal static class ServerHttpRequestRunner
{
    public static async Task RunAsync(HttpRequestTestCase testCase)
    {
        object? capturedInput = null;
        MethodInfo? capturedMethod = null;

        await using var host = await RpcV2CborServerHost
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

        using var request = ServerCborRequestFactory.FromTestCase(
            testCase,
            host.Client.BaseAddress!
        );
        using var response = await host.Client.SendAsync(request).ConfigureAwait(false);
        Assert.True(
            (int)response.StatusCode < 500,
            $"Server returned {(int)response.StatusCode} for {testCase.Id}."
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
            return Task.CompletedTask;

        var resultType = method.ReturnType.GetGenericArguments()[0];
        var result = BuildDefault(resultType);
        return typeof(Task)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Task.FromResult))
            .MakeGenericMethod(resultType)
            .Invoke(null, [result])!;
    }

    private static object? BuildDefault(Type type, int depth = 0)
    {
        if (depth > 6)
            return null;
        if (type == typeof(string))
            return string.Empty;
        if (type.IsValueType)
            return Activator.CreateInstance(type);

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (
                def == typeof(IEnumerable<>)
                || def == typeof(IReadOnlyList<>)
                || def == typeof(List<>)
            )
                return Array.CreateInstance(type.GetGenericArguments()[0], 0);
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                return Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(type.GetGenericArguments())
                );
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
            .Select(p => p.HasDefaultValue ? p.DefaultValue : BuildDefault(p.ParameterType, depth + 1))
            .ToArray();
        return ctor.Invoke(args);
    }
}

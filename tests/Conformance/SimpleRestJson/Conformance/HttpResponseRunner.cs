using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Annotations;

namespace SimpleRestJson.Conformance;

/// <summary>
/// Drives a single httpResponseTests case end-to-end:
///   1. resolves the operation that owns the test (either directly when the trait sits on an
///      operation, or transitively when it sits on an output/error structure);
///   2. constructs a fake HTTP response from the test case (status, headers, body);
///   3. invokes the generated operation through that response and either asserts the returned
///      output or the thrown exception matches the expected {@code params}.
/// </summary>
internal static class HttpResponseRunner
{
    private static readonly Uri Endpoint = new("http://localhost");

    private static readonly IReadOnlyList<Type> ClientTypes =
    [
        .. typeof(NSmithy.Client.SmithyClientOptions).Assembly == null
            ? Array.Empty<Type>()
            : Array.Empty<Type>(),
    ];

    public static async Task RunAsync(HttpResponseTestCase testCase, JsonObject modelShapes)
    {
        var owningOp = ResolveOwningOperation(testCase.ShapeId, modelShapes, out var isError);
        var localOpName = owningOp.Split('#')[^1];

        var (clientType, method) = ConformanceClients.ResolveOperation(localOpName + "Async");
        var inputType = method.GetParameters()[0].ParameterType;
        var input = BuildEmptyInput(inputType);

        var handler = new RecordingHttpMessageHandler(_ => BuildResponse(testCase));
        using var httpClient = new HttpClient(handler);
        var client = Activator.CreateInstance(
            clientType,
            httpClient,
            new SmithyClientOptions { Endpoint = Endpoint }
        )!;

        Exception? thrown = null;
        object? output = null;
        try
        {
            var task = (Task)method.Invoke(client, [input, CancellationToken.None])!;
            await task.ConfigureAwait(false);
            var resultProp = task.GetType().GetProperty("Result")!;
            output = resultProp.GetValue(task);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            thrown = tie.InnerException;
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        if (isError)
        {
            Assert.NotNull(thrown);
            var expectedTypeName = testCase.ShapeId.Split('#')[^1];
            Assert.Equal(expectedTypeName, thrown!.GetType().Name);
            ResponseAssertions.AssertEquivalent(
                testCase.Params,
                thrown,
                expectedTypeName
            );
            return;
        }

        Assert.Null(thrown);
        Assert.NotNull(output);
        ResponseAssertions.AssertEquivalent(
            testCase.Params,
            output!,
            testCase.OperationOrErrorName
        );
    }

    private static HttpResponseMessage BuildResponse(HttpResponseTestCase testCase)
    {
        var msg = new HttpResponseMessage((HttpStatusCode)testCase.Code);
        var bodyBytes = string.IsNullOrEmpty(testCase.Body)
            ? []
            : System.Text.Encoding.UTF8.GetBytes(testCase.Body);
        var content = new ByteArrayContent(bodyBytes);
        if (!string.IsNullOrEmpty(testCase.BodyMediaType))
        {
            content.Headers.TryAddWithoutValidation("Content-Type", testCase.BodyMediaType);
        }
        msg.Content = content;
        foreach (var (name, value) in testCase.Headers)
        {
            // Some headers (Content-Type, Content-Length) belong to Content; everything else to
            // the response. Try both — HttpClient ignores duplicates.
            if (!msg.Headers.TryAddWithoutValidation(name, value))
            {
                msg.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }
        return msg;
    }

    private static string ResolveOwningOperation(
        string shapeId,
        JsonObject shapes,
        out bool isError
    )
    {
        var node = shapes[shapeId] as JsonObject
            ?? throw new InvalidOperationException($"Shape {shapeId} not found in model.");
        var type = (string?)node["type"];
        if (type == "operation")
        {
            isError = false;
            return shapeId;
        }
        // Otherwise: structure — look for an operation whose errors[] references it.
        // (Output structs in this codebase carry the trait on the operation, not the struct.)
        isError = true;
        foreach (var (id, shape) in shapes)
        {
            if (shape is not JsonObject obj || (string?)obj["type"] != "operation")
                continue;
            var errors = obj["errors"] as JsonArray;
            if (errors is null)
                continue;
            foreach (var e in errors)
            {
                if ((string?)(e as JsonObject)?["target"] == shapeId)
                    return id;
            }
        }
        throw new InvalidOperationException(
            $"No operation references error structure {shapeId}; cannot drive the response test."
        );
    }

    private static object BuildEmptyInput(Type inputType)
    {
        var ctor =
            inputType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .First();
        var args = new object?[ctor.GetParameters().Length];
        for (var i = 0; i < args.Length; i++)
        {
            var p = ctor.GetParameters()[i];
            if (p.HasDefaultValue)
                args[i] = p.DefaultValue;
            else if (p.ParameterType.IsValueType)
                args[i] = Activator.CreateInstance(p.ParameterType);
            else if (p.ParameterType == typeof(string))
                args[i] = string.Empty;
            else
                args[i] = null;
        }
        return ctor.Invoke(args);
    }
}

/// <summary>
/// Discovers the generated client classes in the test assembly and resolves operation methods
/// by name. Cached so reflection runs only once per assembly.
/// </summary>
internal static class ConformanceClients
{
    private static readonly Lazy<IReadOnlyList<Type>> Types = new(() =>
        [.. typeof(Alloy.Test.AddMenuItemInput)
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
            )]);

    public static (Type ClientType, MethodInfo Method) ResolveOperation(string methodName)
    {
        foreach (var t in Types.Value)
        {
            var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (m is not null)
                return (t, m);
        }
        throw new InvalidOperationException(
            $"Operation method {methodName} not found on any generated client ("
                + string.Join(", ", Types.Value.Select(t => t.FullName))
                + ")."
        );
    }
}

/// <summary>
/// Walks a generated runtime instance and asserts it is structurally equivalent to a JSON
/// {@code params} blob from a Smithy protocol test. Numeric and timestamp values are compared
/// permissively (numbers compare by value; timestamps tolerate epoch-seconds vs ISO-8601 since
/// the smithy.test fixture emits whichever the modeler chose).
/// </summary>
internal static class ResponseAssertions
{
    public static void AssertEquivalent(JsonNode? expected, object actual, string ownerLabel)
    {
        AssertEqual(expected, actual, ownerLabel);
    }

    private static void AssertEqual(JsonNode? expected, object? actual, string path)
    {
        if (expected is null)
        {
            Assert.True(actual is null, $"[{path}] expected null, got {Describe(actual)}.");
            return;
        }
        if (actual is null)
        {
            Assert.Fail($"[{path}] expected {expected.ToJsonString()}, got null.");
            return;
        }

        // DateTimeOffset: tolerate epoch-seconds (number) or ISO 8601 (string).
        if (actual is DateTimeOffset dto)
        {
            if (expected is JsonValue v)
            {
                if (v.TryGetValue<double>(out var epoch))
                {
                    var actualEpoch = dto.ToUnixTimeMilliseconds() / 1000.0;
                    Assert.True(
                        Math.Abs(actualEpoch - epoch) < 0.001,
                        $"[{path}] timestamp mismatch: expected {epoch}s, got {actualEpoch}s."
                    );
                    return;
                }
                if (v.TryGetValue<string>(out var iso))
                {
                    var parsed = DateTimeOffset.Parse(
                        iso,
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                    Assert.Equal(parsed.ToUniversalTime(), dto.ToUniversalTime());
                    return;
                }
            }
            Assert.Fail($"[{path}] cannot compare DateTimeOffset against {expected.ToJsonString()}.");
            return;
        }

        // Enum-as-struct: compare its Value property to the expected string.
        var actualType = actual.GetType();
        var shape = actualType.GetCustomAttribute<SmithyShapeAttribute>();
        if (actualType.IsValueType && shape?.Kind == ShapeKind.Enum)
        {
            var valProp = actualType.GetProperty("Value")!;
            Assert.Equal((string?)expected, (string?)valProp.GetValue(actual));
            return;
        }

        if (actualType.IsEnum)
        {
            var ev = (JsonValue)expected;
            if (ev.TryGetValue<long>(out var n))
            {
                Assert.Equal(n, Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.Equal((string?)expected, actual.ToString());
            }
            return;
        }

        // Smithy unions: actual is a `MemberName` subclass with a `Value` property. Expected is
        // a single-key JSON object {"memberName": <value>}.
        if (shape?.Kind == ShapeKind.Union)
        {
            var obj = expected.AsObject();
            Assert.Single(obj);
            var (memberName, inner) = obj.First();
            var pascal = char.ToUpperInvariant(memberName[0]) + memberName[1..];
            Assert.Equal(pascal, actualType.Name);
            var valueProp = actualType.GetProperty("Value")!;
            AssertEqual(inner, valueProp.GetValue(actual), $"{path}.{memberName}");
            return;
        }

        // Smithy lists: wrapper record with a `Values` IReadOnlyList<T> property.
        if (shape?.Kind == ShapeKind.List)
        {
            var values = actualType.GetProperty("Values")!.GetValue(actual);
            AssertSequence(expected, (IEnumerable)values!, path);
            return;
        }

        // Plain enumerable (used for IReadOnlyList<T> directly bound on a structure member).
        if (
            actual is IEnumerable seq
            && actual is not string
            && actualType.GetCustomAttribute<SmithyShapeAttribute>()?.Kind != ShapeKind.Structure
        )
        {
            // Maps come through as IEnumerable<KeyValuePair<K, V>>.
            if (
                actualType.IsGenericType
                && (
                    actualType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                    || typeof(System.Collections.IDictionary).IsAssignableFrom(actualType)
                )
            )
            {
                AssertMap(expected, (System.Collections.IDictionary)actual, path);
                return;
            }
            AssertSequence(expected, seq, path);
            return;
        }

        // Primitives.
        if (expected is JsonValue ev2)
        {
            AssertScalar(ev2, actual, path);
            return;
        }

        // Structure: walk ctor parameters, look up expected[paramName].
        AssertStructure(expected.AsObject(), actual, path);
    }

    private static void AssertStructure(JsonObject expected, object actual, string path)
    {
        var type = actual.GetType();
        var ctor =
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .First();
        foreach (var p in ctor.GetParameters())
        {
            var memberName = p.Name!;
            // Property is PascalCase derived from the camelCase ctor param.
            var propName = char.ToUpperInvariant(memberName[0]) + memberName[1..];
            var prop =
                type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"[{path}] Cannot resolve property {propName} on {type.FullName}."
                );
            var actualValue = prop.GetValue(actual);
            if (!expected.TryGetPropertyValue(memberName, out var expectedValue))
            {
                // Member missing in expected params — treat as null/default.
                expectedValue = null;
            }
            // Skip null-vs-null trivially.
            if (expectedValue is null && actualValue is null)
                continue;
            AssertEqual(expectedValue, actualValue, $"{path}.{memberName}");
        }
    }

    private static void AssertSequence(JsonNode expected, IEnumerable actual, string path)
    {
        var arr = expected.AsArray();
        var actualList = actual.Cast<object?>().ToArray();
        Assert.Equal(arr.Count, actualList.Length);
        for (var i = 0; i < arr.Count; i++)
            AssertEqual(arr[i], actualList[i], $"{path}[{i}]");
    }

    private static void AssertMap(JsonNode expected, System.Collections.IDictionary actual, string path)
    {
        var obj = expected.AsObject();
        Assert.Equal(obj.Count, actual.Count);
        foreach (var (key, value) in obj)
        {
            Assert.True(actual.Contains(key), $"[{path}] map missing key '{key}'.");
            AssertEqual(value, actual[key], $"{path}[{key}]");
        }
    }

    private static void AssertScalar(JsonValue expected, object actual, string path)
    {
        if (actual is string s)
        {
            Assert.Equal((string?)expected, s);
            return;
        }
        if (actual is bool b)
        {
            Assert.Equal((bool)expected!, b);
            return;
        }
        if (
            actual is int
            || actual is long
            || actual is short
            || actual is byte
            || actual is float
            || actual is double
            || actual is decimal
        )
        {
            var expectedNum = (double)expected!;
            var actualNum = Convert.ToDouble(actual, System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(
                Math.Abs(actualNum - expectedNum) < 1e-9 || actualNum == expectedNum,
                $"[{path}] expected {expectedNum}, got {actualNum}."
            );
            return;
        }
        Assert.Fail($"[{path}] don't know how to compare scalar {actual.GetType()}.");
    }

    private static string Describe(object? o) => o is null ? "null" : $"{o.GetType().Name}({o})";
}

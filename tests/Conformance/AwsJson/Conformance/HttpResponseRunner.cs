using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using NSmithy.Client;
using NSmithy.Core;
using NSmithy.Core.Serde;

namespace AwsJson.Conformance;

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

    public static async Task RunAsync(HttpResponseTestCase testCase, JsonObject modelShapes)
    {
        var owningOp = ResolveOwningOperation(testCase.ShapeId, modelShapes, out var isError);
        var localOpName = owningOp.Split('#')[^1];

        var (clientType, method) = ConformanceClients.ResolveOperation(localOpName + "Async");
        var inputType = method.GetParameters()[0].ParameterType;
        var input = BuildEmptyInput(inputType);

        var handler = new RecordingHttpMessageHandler(_ => BuildResponse(testCase));
        using var httpClient = new HttpClient(handler);
        var client = ConformanceClients.Build(clientType, httpClient, Endpoint);

        Exception? thrown = null;
        object? output = null;
        try
        {
            var task = (Task)method.Invoke(client, [input, CancellationToken.None])!;
            await task.ConfigureAwait(false);
            var resultProp =
                method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)
                    ? task.GetType().GetProperty("Result")
                    : null;
            output = resultProp is null ? SmithyUnit.Value : resultProp.GetValue(task);
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
            ResponseAssertions.AssertEquivalent(testCase.Params, thrown, expectedTypeName);
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
        var node =
            shapes[shapeId] as JsonObject
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

    private static object BuildEmptyInput(Type inputType) => BuildDefault(inputType, depth: 0)!;

    private static object? BuildDefault(Type type, int depth)
    {
        if (depth > 6)
            return null;
        if (type == typeof(string))
            return string.Empty;
        if (type.IsValueType)
        {
            // Smithy enums are generated as readonly record structs whose Value property is set
            // only by static instances. Pick the first static instance so the codec can read a
            // non-null Value.
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
        // IEnumerable<T> / IReadOnlyList<T> / etc. ctor parameters: hand back an empty array.
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
        // Abstract type (e.g. a Smithy union base): pick the first concrete subclass declared
        // in the same assembly so we can satisfy the ctor's null-guard.
        if (type.IsAbstract)
        {
            var concrete = type
                .Assembly.GetTypes()
                .FirstOrDefault(t => !t.IsAbstract && type.IsAssignableFrom(t));
            return concrete is null ? null : BuildDefault(concrete, depth + 1);
        }
        // For required reference-typed members we need to recursively construct a non-null
        // instance, otherwise the [required] ctor guard rejects null. Generated structures and
        // wrapper records (list/map) all expose a single most-arity public ctor.
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
            return null;
        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            if (p.HasDefaultValue)
                args[i] = p.DefaultValue;
            else
                args[i] = BuildDefault(p.ParameterType, depth + 1);
        }
        return ctor.Invoke(args);
    }

    private static bool IsNullable(ParameterInfo p)
    {
        if (p.ParameterType.IsValueType)
            return Nullable.GetUnderlyingType(p.ParameterType) is not null;
        var ctx = new NullabilityInfoContext();
        return ctx.Create(p).WriteState == NullabilityState.Nullable;
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

/// <summary>
/// Discovers the generated client classes in the test assembly and resolves operation methods
/// by name. Cached so reflection runs only once per assembly.
/// </summary>
internal static class ConformanceClients
{
    private static readonly Lazy<IReadOnlyList<Type>> Types = new(() =>
        [
            .. typeof(HttpResponseRunner)
                .Assembly.GetTypes()
                .Where(t =>
                    t is { IsClass: true, IsAbstract: false }
                    && t.Name.EndsWith("Client", StringComparison.Ordinal)
                    && HasHttpClientConstructor(t)
                ),
        ]
    );

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

    /// <summary>
    /// Constructs a generated client through its HttpClient constructor, using the default protocol.
    /// </summary>
    public static object Build(
        Type clientType,
        HttpClient httpClient,
        Uri endpoint,
        Func<string>? idempotencyTokenProvider = null,
        IClientInterceptor? interceptor = null
    )
    {
        // Use the HttpClient constructor (endpoint comes from BaseAddress) so the recording handler
        // is honoured; this is also the constructor IHttpClientFactory uses. The optional knobs now
        // live on the per-client {Service}ClientConfig; build one reflectively only when needed.
        httpClient.BaseAddress = endpoint;
        object? config = null;
        if (idempotencyTokenProvider is not null || interceptor is not null)
        {
            var configType =
                clientType.Assembly.GetType(clientType.FullName + "Config")
                ?? throw new InvalidOperationException(
                    $"Config type {clientType.FullName}Config not found."
                );
            config = Activator.CreateInstance(configType)!;
            if (idempotencyTokenProvider is not null)
            {
                configType
                    .GetProperty("IdempotencyTokenProvider")!
                    .SetValue(config, idempotencyTokenProvider);
            }

            if (interceptor is not null)
            {
                ((SmithyClientConfig)config).Interceptors.Add(interceptor);
            }
        }
        return Activator.CreateInstance(clientType, [httpClient, config])!;
    }

    private static bool HasHttpClientConstructor(Type clientType) =>
        clientType
            .GetConstructors()
            .Any(c =>
                c.GetParameters() is [{ ParameterType: var p }, ..] && p == typeof(HttpClient)
            );
}

/// <summary>
/// Walks a generated runtime instance and asserts it is structurally equivalent to a JSON
/// {@code params} blob from a Smithy protocol test. Numeric and timestamp values are compared
/// permissively (numbers compare by value; timestamps tolerate epoch-seconds vs ISO-8601 since
/// the smithy.test fixture emits whichever the modeler chose).
/// </summary>
internal static class ResponseAssertions
{
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

    public static void AssertEquivalent(JsonNode? expected, object actual, string ownerLabel)
    {
        // A missing `params` field on the test case means "no expected output" — the operation
        // simply has to succeed. Generated empty-output structures will materialize as an empty
        // record instance (not null), so we can't compare against a null expected.
        if (expected is null)
            return;
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
            Assert.Fail(
                $"[{path}] cannot compare DateTimeOffset against {expected.ToJsonString()}."
            );
            return;
        }

        // Enum-as-struct: compare its Value property to the expected string.
        var actualType = actual.GetType();
        var shape = GetSchemaKind(actualType);
        if (actualType.IsValueType && shape == ShapeKind.Enum)
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
                Assert.Equal(
                    n,
                    Convert.ToInt64(actual, System.Globalization.CultureInfo.InvariantCulture)
                );
            }
            else
            {
                Assert.Equal((string?)expected, actual.ToString());
            }
            return;
        }

        // Smithy unions: actual is a `MemberName` subclass with a `Value` property. Expected is
        // a single-key JSON object {"memberName": <value>}.
        if (shape == ShapeKind.Union)
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
        if (shape == ShapeKind.List)
        {
            var values = actualType.GetProperty("Values")!.GetValue(actual);
            AssertSequence(expected, (IEnumerable)values!, path);
            return;
        }

        // Smithy maps: wrapper record with a `Values` IReadOnlyDictionary<K,V> property; the
        // expected JSON is the dictionary contents directly (no `values` key).
        if (shape == ShapeKind.Map)
        {
            var values = actualType.GetProperty("Values")!.GetValue(actual);
            AssertMap(expected, ToDictionary(values!), path);
            return;
        }

        // byte[] implements IEnumerable<byte> but must be compared as a base64 blob.
        // A streaming blob arrives as a Stream rather than a byte[]. Drain it and let
        // the blob comparison below handle it, otherwise it reaches the scalar branch
        // and fails with "don't know how to compare scalar ...Stream".
        if (actual is Stream stream)
        {
            using var drained = new MemoryStream();
            stream.CopyTo(drained);
            actual = drained.ToArray();
        }

        if (actual is byte[] bytes)
        {
            var base64 = (string?)expected;
            byte[] expectedBytes;
            if (string.IsNullOrEmpty(base64))
            {
                expectedBytes = [];
            }
            else
            {
                try
                {
                    expectedBytes = Convert.FromBase64String(base64);
                }
                catch (FormatException)
                {
                    // Raw payload/blob protocol tests often express the expected body as plain text.
                    expectedBytes = System.Text.Encoding.UTF8.GetBytes(base64);
                }
            }
            Assert.Equal(expectedBytes, bytes);
            return;
        }

        if (actual is Document document)
        {
            AssertJsonEqual(expected, DocumentToJson(document), path);
            return;
        }

        // Plain enumerable (used for IReadOnlyList<T> directly bound on a structure member).
        if (
            actual is IEnumerable seq
            && actual is not string
            && GetSchemaKind(actualType) != ShapeKind.Structure
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
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        foreach (var p in ctor.GetParameters())
        {
            // Generated records carry PascalCase constructor parameters, while a test
            // fixture's `params` uses the model's camelCase member names. Matching them
            // ordinally silently skipped every member, turning this whole assertion
            // into a no-op — a wrong expected value passed.
            var propName = p.Name!;
            var prop =
                type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)
                // Generated records name the constructor parameter exactly as the
                // property, but a union case class takes a camelCase parameter and
                // exposes a PascalCase property, so both spellings have to resolve.
                ?? type.GetProperty(
                    char.ToUpperInvariant(propName[0]) + propName[1..],
                    BindingFlags.Public | BindingFlags.Instance
                )
                ?? throw new InvalidOperationException(
                    $"[{path}] Cannot resolve property {propName} on {type.FullName}."
                );
            var actualValue = prop.GetValue(actual);
            var memberName = ResolveExpectedKey(expected, propName);
            if (memberName is null)
            {
                // Member absent from the test fixture's `params`. Smithy protocol-test
                // semantics: omitted fields are not asserted (could be null, default, or just
                // "don't care"). Skip the comparison entirely.
                continue;
            }

            AssertEqual(expected[memberName], actualValue, $"{path}.{memberName}");
        }
    }

    /// <summary>
    /// Finds the fixture key matching a generated property, tolerating the
    /// PascalCase/camelCase difference between generated records and model member
    /// names. Returns null when the member is genuinely absent from `params`, which
    /// Smithy protocol-test semantics treat as "not asserted".
    /// </summary>
    private static string? ResolveExpectedKey(JsonObject expected, string propName)
    {
        if (expected.ContainsKey(propName))
        {
            return propName;
        }

        foreach (var (key, _) in expected)
        {
            if (string.Equals(key, propName, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    private static void AssertSequence(JsonNode expected, IEnumerable actual, string path)
    {
        var arr = expected.AsArray();
        var actualList = actual.Cast<object?>().ToArray();
        if (
            arr.Count == 0
            && actualList.Length == 1
            && actualList[0] is string empty
            && empty.Length == 0
        )
        {
            actualList = [];
        }
        Assert.Equal(arr.Count, actualList.Length);
        for (var i = 0; i < arr.Count; i++)
            AssertEqual(arr[i], actualList[i], $"{path}[{i}]");
    }

    private static JsonNode? DocumentToJson(Document document) =>
        document.Kind switch
        {
            DocumentKind.Null => null,
            DocumentKind.Boolean => JsonValue.Create(document.AsBoolean()),
            DocumentKind.String => JsonValue.Create(document.AsString()),
            DocumentKind.Number => JsonValue.Create(document.AsNumber()),
            DocumentKind.Array => new JsonArray(
                document.AsArray().Select(DocumentToJson).ToArray()
            ),
            DocumentKind.Object => new JsonObject(
                document.AsObject().ToDictionary(kv => kv.Key, kv => DocumentToJson(kv.Value))
            ),
            _ => throw new InvalidOperationException($"Unsupported document kind {document.Kind}."),
        };

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected is null, actual is null);
            return;
        }
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            Assert.Equal(expectedObject.Count, actualObject.Count);
            foreach (var (key, value) in expectedObject)
            {
                Assert.True(
                    actualObject.TryGetPropertyValue(key, out var actualValue),
                    $"[{path}] missing key '{key}'."
                );
                AssertJsonEqual(value, actualValue, $"{path}.{key}");
            }
            return;
        }
        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            Assert.Equal(expectedArray.Count, actualArray.Count);
            for (var i = 0; i < expectedArray.Count; i++)
                AssertJsonEqual(expectedArray[i], actualArray[i], $"{path}[{i}]");
            return;
        }
        if (expected is JsonValue expectedValue && actual is JsonValue actualValueNode)
        {
            if (expectedValue.TryGetValue<string>(out var expectedString))
            {
                Assert.Equal(expectedString, actualValueNode.ToString());
                return;
            }
            if (expectedValue.TryGetValue<bool>(out var expectedBool))
            {
                Assert.True(actualValueNode.TryGetValue<bool>(out var actualBool));
                Assert.Equal(expectedBool, actualBool);
                return;
            }
            if (expectedValue.TryGetValue<decimal>(out var expectedDecimal))
            {
                Assert.True(actualValueNode.TryGetValue<decimal>(out var actualDecimal));
                Assert.Equal(expectedDecimal, actualDecimal);
                return;
            }
        }
        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
    }

    private static System.Collections.IDictionary ToDictionary(object enumerableOrDictionary)
    {
        if (enumerableOrDictionary is System.Collections.IDictionary dict)
            return dict;
        // IReadOnlyDictionary<K,V> isn't IDictionary, so project to a plain Hashtable.
        var result = new System.Collections.Hashtable();
        var t = enumerableOrDictionary.GetType();
        var keysProp = t.GetProperty("Keys");
        var indexer = t.GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length == 1);
        if (keysProp is not null && indexer is not null)
        {
            foreach (var k in (IEnumerable)keysProp.GetValue(enumerableOrDictionary)!)
                result[k!] = indexer.GetValue(enumerableOrDictionary, [k]);
            return result;
        }
        // Fallback: enumerate as KeyValuePair<,>.
        foreach (var kv in (IEnumerable)enumerableOrDictionary)
        {
            var kvType = kv!.GetType();
            var k = kvType.GetProperty("Key")!.GetValue(kv)!;
            var v = kvType.GetProperty("Value")!.GetValue(kv);
            result[k] = v;
        }
        return result;
    }

    private static void AssertMap(
        JsonNode expected,
        System.Collections.IDictionary actual,
        string path
    )
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
            || actual is sbyte
            || actual is float
            || actual is double
            || actual is decimal
        )
        {
            var actualNum = Convert.ToDouble(
                actual,
                System.Globalization.CultureInfo.InvariantCulture
            );
            // NaN and ±Infinity come as JSON strings in awsJson
            if (expected.TryGetValue<string>(out var specialStr))
            {
                var expectedSpecial = specialStr switch
                {
                    "NaN" => double.NaN,
                    "Infinity" => double.PositiveInfinity,
                    "-Infinity" => double.NegativeInfinity,
                    _ => double.Parse(
                        specialStr,
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                };
                if (double.IsNaN(expectedSpecial))
                    Assert.True(
                        double.IsNaN(actualNum),
                        $"[{path}] expected NaN, got {actualNum}."
                    );
                else if (double.IsPositiveInfinity(expectedSpecial))
                    Assert.True(
                        double.IsPositiveInfinity(actualNum),
                        $"[{path}] expected Infinity, got {actualNum}."
                    );
                else if (double.IsNegativeInfinity(expectedSpecial))
                    Assert.True(
                        double.IsNegativeInfinity(actualNum),
                        $"[{path}] expected -Infinity, got {actualNum}."
                    );
                else
                    Assert.Equal(expectedSpecial, actualNum);
                return;
            }
            var expectedNum = (double)expected!;
            // float values lose precision when widened to double; compare at float precision.
            if (actual is float f)
            {
                var expectedF = (float)expectedNum;
                var tolerance = Math.Abs(expectedF) * 1e-6f + 1e-6f;
                Assert.True(
                    Math.Abs(f - expectedF) <= tolerance,
                    $"[{path}] expected {expectedNum}, got {actualNum}."
                );
                return;
            }
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

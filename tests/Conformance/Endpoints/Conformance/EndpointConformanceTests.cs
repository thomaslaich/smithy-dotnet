using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSmithy.Client;
using NSmithy.Core;

namespace Endpoints.Conformance;

/// <summary>
/// Runs every <c>@endpointTests</c> case against the generated resolvers: the case's
/// <c>params</c> through the static <c>Resolve</c>, and each of its <c>operationInputs</c> through
/// a resolver built from client parameters and a bound operation input.
/// </summary>
public sealed class EndpointConformanceTests
{
    public static TheoryData<string, int> Cases()
    {
        var data = new TheoryData<string, int>();
        var model = EndpointTestModel.Current;
        foreach (var serviceId in model.ServiceIds)
        {
            for (var i = 0; i < model.TestCases(serviceId).Count; i++)
            {
                data.Add(serviceId, i);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParamsResolveToTheExpectedOutcome(string serviceId, int index)
    {
        var testCase = EndpointTestModel.Current.TestCases(serviceId)[index]!.AsObject();
        var service = GeneratedService.For(serviceId);
        var parameters = service.Parameters(testCase["params"]?.AsObject() ?? []);

        AssertOutcome(testCase, () => service.Resolve(parameters));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void OperationInputsResolveToTheExpectedOutcome(string serviceId, int index)
    {
        var testCase = EndpointTestModel.Current.TestCases(serviceId)[index]!.AsObject();
        if (testCase["operationInputs"] is not JsonArray operationInputs)
        {
            return;
        }
        var service = GeneratedService.For(serviceId);
        foreach (var operationInput in operationInputs)
        {
            var clientParameters = service.ClientParameters(testCase, operationInput!.AsObject());
            var operationName = (string)operationInput["operationName"]!;
            var input = service.Input(operationName, operationInput["operationParams"]?.AsObject());

            SmithyEndpoint Resolve()
            {
                var resolver = service.Resolver(clientParameters);
                return resolver
                    .ResolveEndpointAsync(
                        new SmithyEndpointParameters(
                            ShapeId.Parse(serviceId),
                            ShapeId.Parse($"{ShapeId.Parse(serviceId).Namespace}#{operationName}"),
                            null,
                            input
                        )
                    )
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }

            AssertOutcome(testCase, Resolve);
        }
    }

    private static void AssertOutcome(JsonObject testCase, Func<SmithyEndpoint> resolve)
    {
        var expect = testCase["expect"]!.AsObject();
        if (expect["error"] is JsonNode error)
        {
            var exception = Assert.Throws<SmithyEndpointResolutionException>(() => resolve());
            Assert.Equal((string)error!, exception.Message);
            return;
        }

        var expected = expect["endpoint"]!.AsObject();
        var endpoint = resolve();
        Assert.Equal((string)expected["url"]!, endpoint.Uri.OriginalString);
        AssertHeaders(expected["headers"]?.AsObject(), endpoint.Headers);
        AssertProperties(expected["properties"]?.AsObject(), endpoint);
    }

    private static void AssertHeaders(
        JsonObject? expected,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? actual
    )
    {
        var expectedHeaders = (expected ?? []).ToDictionary(
            header => header.Key,
            header => header.Value!.AsArray().Select(v => (string)v!).ToArray(),
            StringComparer.OrdinalIgnoreCase
        );
        Assert.Equal(expectedHeaders.Count, actual?.Count ?? 0);
        foreach (var (name, values) in expectedHeaders)
        {
            Assert.NotNull(actual);
            Assert.Equal(values, actual![name]);
        }
    }

    private static void AssertProperties(JsonObject? expected, SmithyEndpoint endpoint)
    {
        var actual = new JsonObject();
        if (endpoint.AuthSchemes is { } authSchemes)
        {
            var schemes = new JsonArray();
            foreach (var scheme in authSchemes)
            {
                var entry = new JsonObject { ["name"] = RuleSchemeName(scheme.SchemeId) };
                foreach (
                    var (key, value) in scheme.Properties ?? new Dictionary<string, Document>()
                )
                {
                    entry[key] = ToJson(value);
                }
                schemes.Add(entry);
            }
            actual["authSchemes"] = schemes;
        }
        foreach (var (key, value) in endpoint.Properties ?? new Dictionary<string, Document>())
        {
            actual[key] = ToJson(value);
        }

        Assert.True(
            JsonNode.DeepEquals(expected ?? [], actual),
            $"Expected properties {expected?.ToJsonString() ?? "{}"} but got {actual.ToJsonString()}"
        );
    }

    // Generated resolvers key AWS schemes by their auth trait id; the cases use the rule names.
    private static string RuleSchemeName(string schemeId) =>
        schemeId switch
        {
            "aws.auth#sigv4" => "sigv4",
            "aws.auth#sigv4a" => "sigv4a",
            _ => schemeId,
        };

    private static JsonNode? ToJson(Document document) =>
        document.Kind switch
        {
            DocumentKind.Null => null,
            DocumentKind.Boolean => JsonValue.Create(document.AsBoolean()),
            DocumentKind.String => JsonValue.Create(document.AsString()),
            DocumentKind.Number => JsonValue.Create(document.AsNumber()),
            DocumentKind.Array => new JsonArray([.. document.AsArray().Select(ToJson)]),
            DocumentKind.Object => new JsonObject(
                document.AsObject().Select(p => KeyValuePair.Create(p.Key, ToJson(p.Value)))
            ),
            _ => throw new InvalidOperationException(document.Kind.ToString()),
        };

    /// <summary>The generated parameters and resolver types of one service, found by name.</summary>
    private sealed class GeneratedService
    {
        private readonly string serviceId;
        private readonly string csharpNamespace;
        private readonly Type parametersType;
        private readonly Type resolverType;

        private GeneratedService(string serviceId)
        {
            this.serviceId = serviceId;
            var id = ShapeId.Parse(serviceId);
            // Each service is generated in its own projection with its name as base namespace.
            csharpNamespace =
                id.Name + "." + string.Join('.', id.Namespace.Split('.').Select(Pascal));
            var assembly = typeof(EndpointConformanceTests).Assembly;
            parametersType = assembly
                .GetTypes()
                .Single(t =>
                    t.Namespace == csharpNamespace
                    && t.Name.EndsWith("EndpointParameters", StringComparison.Ordinal)
                );
            resolverType = assembly
                .GetTypes()
                .Single(t =>
                    t.Namespace == csharpNamespace
                    && t.Name.EndsWith("EndpointResolver", StringComparison.Ordinal)
                );
        }

        public static GeneratedService For(string serviceId) => new(serviceId);

        public object Parameters(JsonObject values)
        {
            var parameters = Activator.CreateInstance(parametersType)!;
            foreach (var (name, value) in values)
            {
                Set(parameters, name, value);
            }
            return parameters;
        }

        /// <summary>
        /// Client-level parameters of an operation input: built-ins and client context
        /// parameters, applied over the rule defaults.
        /// </summary>
        public object ClientParameters(JsonObject testCase, JsonObject operationInput)
        {
            var parameters = Activator.CreateInstance(parametersType)!;
            var model = EndpointTestModel.Current;
            var definitions = model.Parameters(serviceId);
            // Some cases state a client context parameter only in their params; it is client
            // configuration all the same.
            var caseParams = testCase["params"]?.AsObject() ?? [];
            foreach (var name in model.ClientContextParameters(serviceId))
            {
                if (caseParams.TryGetPropertyValue(name, out var value))
                {
                    Set(parameters, name, value);
                }
            }
            foreach (var (builtIn, value) in operationInput["builtInParams"]?.AsObject() ?? [])
            {
                var name = definitions.First(d => (string?)d.Value!["builtIn"] == builtIn).Key;
                Set(parameters, name, value);
            }
            foreach (var (name, value) in operationInput["clientParams"]?.AsObject() ?? [])
            {
                Set(parameters, name, value);
            }
            return parameters;
        }

        public SmithyEndpoint Resolve(object parameters)
        {
            try
            {
                return (SmithyEndpoint)
                    resolverType
                        .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!
                        .Invoke(null, [parameters])!;
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }

        public IEndpointResolver Resolver(object clientParameters)
        {
            try
            {
                return (IEndpointResolver)Activator.CreateInstance(resolverType, clientParameters)!;
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                throw e.InnerException;
            }
        }

        public object? Input(string operationName, JsonObject? values)
        {
            var type = typeof(EndpointConformanceTests).Assembly.GetType(
                $"{csharpNamespace}.{operationName}Input"
            );
            return type is null ? null : InputBinder.Bind(type, values ?? []);
        }

        private void Set(object parameters, string name, JsonNode? value)
        {
            var property =
                parametersType.GetProperty(Pascal(name))
                ?? throw new InvalidOperationException(
                    $"{parametersType.Name} has no property for parameter {name}."
                );
            property.SetValue(parameters, Convert(value, property.PropertyType));
        }

        private static object? Convert(JsonNode? value, Type type)
        {
            if (value is null)
            {
                return null;
            }
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(string))
            {
                return (string)value!;
            }
            if (target == typeof(bool))
            {
                return (bool)value!;
            }
            return value.AsArray().Select(v => (string)v!).ToArray();
        }
    }

    internal static string Pascal(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        var capitalize = true;
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                capitalize = true;
                continue;
            }
            if (sb.Length == 0 && char.IsDigit(c))
            {
                sb.Append('_');
            }
            sb.Append(capitalize ? char.ToUpperInvariant(c) : c);
            capitalize = false;
        }
        return sb.ToString();
    }
}

/// <summary>
/// Builds a generated operation input from test JSON: structures through their constructor,
/// with parameters matched by name; enums through <c>FromValue</c>; unions through their
/// <c>From{Member}</c> factories.
/// </summary>
internal static class InputBinder
{
    public static object? Bind(Type type, JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return (string)value!;
        }
        if (target == typeof(bool))
        {
            return (bool)value!;
        }
        if (target == typeof(int))
        {
            return (int)value!;
        }
        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var element = target.GetGenericArguments()[0];
            var array = Array.CreateInstance(element, value.AsArray().Count);
            for (var i = 0; i < array.Length; i++)
            {
                array.SetValue(Bind(element, value[i]), i);
            }
            return array;
        }
        if (
            target.IsGenericType
            && target.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
        )
        {
            var valueType = target.GetGenericArguments()[1];
            var dictionary = (System.Collections.IDictionary)
                Activator.CreateInstance(
                    typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType)
                )!;
            foreach (var (key, item) in value.AsObject())
            {
                dictionary[key] = Bind(valueType, item);
            }
            return dictionary;
        }
        if (
            value is JsonValue enumValue
            && target.GetMethod("FromValue", BindingFlags.Public | BindingFlags.Static)
                is { } fromValue
        )
        {
            return fromValue.Invoke(
                null,
                [Bind(fromValue.GetParameters()[0].ParameterType, enumValue)]
            );
        }
        if (target.IsAbstract)
        {
            var (member, item) = value.AsObject().Single();
            var factory = target.GetMethod(
                "From" + EndpointConformanceTests.Pascal(member),
                BindingFlags.Public | BindingFlags.Static
            )!;
            return factory.Invoke(null, [Bind(factory.GetParameters()[0].ParameterType, item)]);
        }

        var constructor = target
            .GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();
        var fields = value.AsObject();
        var arguments = constructor
            .GetParameters()
            .Select(p =>
            {
                var field = fields.FirstOrDefault(f =>
                    string.Equals(f.Key, p.Name, StringComparison.OrdinalIgnoreCase)
                );
                return field.Key is null
                    ? p.DefaultValue is DBNull
                        ? null
                        : p.DefaultValue
                    : Bind(p.ParameterType, field.Value);
            })
            .ToArray();
        return constructor.Invoke(arguments);
    }
}

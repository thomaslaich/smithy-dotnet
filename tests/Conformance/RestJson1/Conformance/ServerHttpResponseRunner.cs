using System.Reflection;
using System.Text.Json.Nodes;
using NSmithy.Client;

namespace RestJson1.Conformance;

internal static class ServerHttpResponseRunner
{
    public static async Task RunAsync(HttpResponseTestCase testCase, JsonObject modelShapes)
    {
        var owningOperation = ResolveOwningOperation(testCase.ShapeId, modelShapes);
        var localOpName = owningOperation.Split('#')[^1];
        var (clientType, clientMethod) = ConformanceClients.ResolveOperation(localOpName + "Async");
        var inputType = clientMethod.GetParameters()[0].ParameterType;
        var outputType = clientMethod.ReturnType.GetGenericArguments()[0];
        var output = ParamBinder.Bind(outputType, testCase.Params ?? new JsonObject())!;
        var generatedRequest = await CaptureGeneratedRequestAsync(
                clientType,
                clientMethod,
                ConformanceObjectFactory.BuildDefault(inputType)!
            )
            .ConfigureAwait(false);

        await using var host = await RestJsonServerHost
            .StartAsync(
                (method, args) =>
                {
                    Assert.Equal(localOpName + "Async", method.Name);
                    return CreateResult(method, output);
                }
            )
            .ConfigureAwait(false);

        using var request = CreateReplayRequest(generatedRequest);
        using var response = await host.Client.SendAsync(request).ConfigureAwait(false);
        await ServerResponseAssertions.AssertAsync(testCase, response).ConfigureAwait(false);
    }

    private static async Task<RecordedRequest> CaptureGeneratedRequestAsync(
        Type clientType,
        MethodInfo clientMethod,
        object input
    )
    {
        var handler = new RecordingHttpMessageHandler(_ => RecordingHttpMessageHandler.EmptyOk());
        using var httpClient = new HttpClient(handler);
        var client = Activator.CreateInstance(
            clientType,
            httpClient,
            new SmithyClientOptions { Endpoint = new Uri("http://localhost") }
        )!;

        try
        {
            var task = (Task)clientMethod.Invoke(client, [input, CancellationToken.None])!;
            await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            if (handler.Captured is null)
                throw tie.InnerException;
        }
        catch
        {
            if (handler.Captured is null)
                throw;
        }

        return handler.Captured
            ?? throw new InvalidOperationException(
                "Client did not emit a request for the server replay."
            );
    }

    private static HttpRequestMessage CreateReplayRequest(RecordedRequest recorded)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(recorded.Method),
            recorded.RequestUri.PathAndQuery
        );

        ByteArrayContent? content = null;
        if (recorded.Body.Length > 0 || recorded.ContentType is not null)
        {
            content = new ByteArrayContent(recorded.Body);
            request.Content = content;
        }

        foreach (var (name, values) in recorded.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, values);
        }

        if (content is not null)
        {
            if (recorded.ContentType is not null)
            {
                content.Headers.TryAddWithoutValidation("Content-Type", recorded.ContentType);
            }
        }

        return request;
    }

    private static object CreateResult(MethodInfo method, object output)
    {
        if (method.ReturnType == typeof(Task))
        {
            return Task.CompletedTask;
        }

        return typeof(Task)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Task.FromResult))
            .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0])
            .Invoke(null, [output])!;
    }

    private static string ResolveOwningOperation(string shapeId, JsonObject shapes)
    {
        var node =
            shapes[shapeId] as JsonObject
            ?? throw new InvalidOperationException($"Shape {shapeId} not found in model.");
        if ((string?)node["type"] == "operation")
        {
            return shapeId;
        }

        foreach (var (id, shape) in shapes)
        {
            if (shape is not JsonObject obj || (string?)obj["type"] != "operation")
                continue;
            if ((string?)(obj["output"] as JsonObject)?["target"] == shapeId)
                return id;
            var errors = obj["errors"] as JsonArray;
            if (errors is null)
                continue;
            foreach (var error in errors)
            {
                if ((string?)(error as JsonObject)?["target"] == shapeId)
                    return id;
            }
        }

        throw new InvalidOperationException(
            $"No operation references response shape {shapeId}; cannot drive the server response test."
        );
    }
}

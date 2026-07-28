using System.Net;
using NSmithy.Client;
using NSmithy.Http;

namespace NSmithy.Tests.Client;

public sealed class DebugInterceptorTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoHeaders =
        new Dictionary<string, IReadOnlyList<string>>();

    private static SmithyContext NewContext()
    {
        var context = new SmithyContext();
        context.Set(SmithyContextKeys.ServiceName, "Weather");
        context.Set(SmithyContextKeys.OperationName, "GetCity");
        context.Set(SmithyContextKeys.Attempt, 2);
        return context;
    }

    [Fact]
    public async Task RequestLogIncludesMethodUriHeadersAndHexDump()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        var request = new SmithyHttpRequest(HttpMethod.Post, "/service/Weather/operation/GetCity")
        {
            Body = new SmithyHttpBody.Bytes("hello"u8.ToArray()),
        };
        request.Headers["smithy-protocol"] = ["rpc-v2-cbor"];

        var result = await interceptor.OnBeforeTransmitAsync(NewContext(), request);

        Assert.Same(request, result);
        var text = output.ToString();
        Assert.Contains("[Weather.GetCity]", text, StringComparison.Ordinal);
        Assert.Contains("POST /service/Weather/operation/GetCity", text, StringComparison.Ordinal);
        Assert.Contains("attempt 2", text, StringComparison.Ordinal);
        Assert.Contains("smithy-protocol: rpc-v2-cbor", text, StringComparison.Ordinal);
        Assert.Contains("body: 5 bytes", text, StringComparison.Ordinal);
        Assert.Contains("68 65 6c 6c 6f", text, StringComparison.Ordinal);
        Assert.Contains("hello", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactsSensitiveHeaders()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        var request = new SmithyHttpRequest(HttpMethod.Get, "/cities/SEA");
        request.Headers["Authorization"] = ["Bearer super-secret"];
        request.Headers["Accept"] = ["application/json"];

        await interceptor.OnBeforeTransmitAsync(NewContext(), request);

        var text = output.ToString();
        Assert.Contains("Authorization: <redacted>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", text, StringComparison.Ordinal);
        Assert.Contains("Accept: application/json", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatesBodyHexDumpAtMaxBodyBytes()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output, MaxBodyBytes = 16 };
        var request = new SmithyHttpRequest(HttpMethod.Post, "/upload")
        {
            Body = new SmithyHttpBody.Bytes(new byte[40]),
        };

        await interceptor.OnBeforeTransmitAsync(NewContext(), request);

        var text = output.ToString();
        Assert.Contains("body: 40 bytes", text, StringComparison.Ordinal);
        Assert.Contains("... 24 more bytes", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotConsumeStreamingBodies()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        using var stream = new MemoryStream("streamed"u8.ToArray());
        var request = new SmithyHttpRequest(HttpMethod.Put, "/blob")
        {
            Body = new SmithyHttpBody.Streaming(stream, 8),
        };

        await interceptor.OnBeforeTransmitAsync(NewContext(), request);

        Assert.Equal(0, stream.Position);
        Assert.Contains("body: <streaming, 8 bytes>", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseLogIncludesStatusAndBody()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        var response = new SmithyHttpClientResponse(
            HttpStatusCode.OK,
            "OK",
            "{}"u8.ToArray(),
            NoHeaders,
            NoHeaders
        );

        interceptor.OnAfterTransmit(NewContext(), response);

        var text = output.ToString();
        Assert.Contains("response (attempt 2): 200 OK", text, StringComparison.Ordinal);
        Assert.Contains("7b 7d", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyBodiesAreLoggedAsEmpty()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        var response = new SmithyHttpClientResponse(
            HttpStatusCode.NoContent,
            "No Content",
            [],
            NoHeaders,
            NoHeaders
        );

        interceptor.OnAfterTransmit(NewContext(), response);

        Assert.Contains("body: <empty>", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LogsTypedValuesAndOutcome()
    {
        using var output = new StringWriter();
        var interceptor = new DebugInterceptor { Output = output };
        var context = NewContext();

        interceptor.OnBeforeSerialization(context, new TestInput("SEA"));
        interceptor.OnAfterDeserialization(context, new TestOutput("Seattle"));
        interceptor.OnAfterExecution(context, null);
        interceptor.OnAfterExecution(context, new InvalidOperationException("boom"));

        var text = output.ToString();
        Assert.Contains("input: TestInput { CityId = SEA }", text, StringComparison.Ordinal);
        Assert.Contains("output: TestOutput { Name = Seattle }", text, StringComparison.Ordinal);
        Assert.Contains("[Weather.GetCity] completed", text, StringComparison.Ordinal);
        Assert.Contains(
            "[Weather.GetCity] failed: InvalidOperationException: boom",
            text,
            StringComparison.Ordinal
        );
    }

    private sealed record TestInput(string CityId);

    private sealed record TestOutput(string Name);
}

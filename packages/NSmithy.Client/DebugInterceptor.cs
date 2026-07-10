using System.Globalization;
using System.Text;
using NSmithy.Http;
using static System.FormattableString;

namespace NSmithy.Client;

/// <summary>
/// Logs each stage of an operation execution to <see cref="Output"/>: the typed
/// input and output, every transport attempt's HTTP request and response, and a
/// hex dump of the body bytes. Useful for debugging and for inspecting what a
/// protocol puts on the wire; not intended as a production logging solution.
/// </summary>
public sealed class DebugInterceptor : IClientInterceptor
{
    /// <summary>Where log lines are written. Defaults to standard output.</summary>
    public TextWriter Output { get; init; } = Console.Out;

    /// <summary>Maximum number of body bytes rendered per hex dump. Defaults to 1024.</summary>
    public int MaxBodyBytes { get; init; } = 1024;

    /// <summary>
    /// Header names whose values are logged as <c>&lt;redacted&gt;</c>. Contains
    /// <c>Authorization</c>, <c>Proxy-Authorization</c>, and <c>X-Api-Key</c> by default.
    /// </summary>
    public ISet<string> RedactedHeaders { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "X-Api-Key",
        };

    public void OnBeforeSerialization(SmithyContext context, object? input) =>
        Output.WriteLine(Invariant($"{Label(context)} input: {input ?? "<null>"}"));

    public ValueTask<SmithyHttpRequest> OnBeforeTransmitAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var text = new StringBuilder();
        text.Append(
            CultureInfo.InvariantCulture,
            $"{Label(context)} request (attempt {Attempt(context)}): {request.Method} {request.RequestUri}"
        );
        if (context.TryGet(SmithyContextKeys.Endpoint, out Uri endpoint))
        {
            text.Append(CultureInfo.InvariantCulture, $" (endpoint {endpoint})");
        }

        text.AppendLine();
        AppendHeaders(text, request.Headers);
        AppendHeaders(text, request.ContentHeaders);
        if (request.ContentType is { } contentType)
        {
            text.AppendLine(Invariant($"  content-type: {contentType}"));
        }

        AppendBody(text, request.Body);
        Output.Write(text);
        return ValueTask.FromResult(request);
    }

    public void OnAfterTransmit(SmithyContext context, SmithyHttpResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine(
            Invariant(
                $"{Label(context)} response (attempt {Attempt(context)}): {(int)response.StatusCode} {response.ReasonPhrase}"
            )
        );
        AppendHeaders(text, response.Headers);
        AppendHeaders(text, response.ContentHeaders);
        AppendBody(text, response.Body);
        Output.Write(text);
    }

    public void OnAfterDeserialization(SmithyContext context, object? output) =>
        Output.WriteLine(Invariant($"{Label(context)} output: {output ?? "<null>"}"));

    public void OnAfterExecution(SmithyContext context, Exception? exception) =>
        Output.WriteLine(
            exception is null
                ? Invariant($"{Label(context)} completed")
                : Invariant(
                    $"{Label(context)} failed: {exception.GetType().Name}: {exception.Message}"
                )
        );

    private static string Label(SmithyContext context)
    {
        var service = context.TryGet(SmithyContextKeys.ServiceName, out string name) ? name : "?";
        var operation = context.TryGet(SmithyContextKeys.OperationName, out string op) ? op : "?";
        return Invariant($"[{service}.{operation}]");
    }

    private static int Attempt(SmithyContext context) =>
        context.TryGet(SmithyContextKeys.Attempt, out int attempt) ? attempt : 1;

    private void AppendHeaders(
        StringBuilder text,
        IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> headers
    )
    {
        foreach (var (name, values) in headers)
        {
            var rendered = RedactedHeaders.Contains(name)
                ? "<redacted>"
                : string.Join(", ", values);
            text.AppendLine(Invariant($"  {name}: {rendered}"));
        }
    }

    private void AppendBody(StringBuilder text, SmithyHttpBody body)
    {
        switch (body)
        {
            case SmithyHttpBody.Bytes bytes:
                text.AppendLine(Invariant($"  body: {bytes.Content.Length} bytes"));
                AppendHexDump(text, bytes.Content);
                break;
            case SmithyHttpBody.Streaming streaming:
                text.AppendLine(
                    streaming.ContentLength is { } length
                        ? Invariant($"  body: <streaming, {length} bytes>")
                        : "  body: <streaming, unknown length>"
                );
                break;
            default:
                text.AppendLine("  body: <empty>");
                break;
        }
    }

    private void AppendHexDump(StringBuilder text, byte[] bytes)
    {
        var shown = Math.Min(bytes.Length, Math.Max(0, MaxBodyBytes));
        for (var offset = 0; offset < shown; offset += 16)
        {
            var line = bytes.AsSpan(offset, Math.Min(16, shown - offset));
            text.Append(CultureInfo.InvariantCulture, $"    {offset:x8}  ");
            for (var i = 0; i < 16; i++)
            {
                text.Append(
                    i < line.Length ? line[i].ToString("x2", CultureInfo.InvariantCulture) : "  "
                );
                text.Append(' ');
            }

            text.Append(' ');
            foreach (var value in line)
            {
                text.Append(value is >= 0x20 and < 0x7f ? (char)value : '.');
            }

            text.AppendLine();
        }

        if (bytes.Length > shown)
        {
            text.AppendLine(Invariant($"    ... {bytes.Length - shown} more bytes"));
        }
    }
}

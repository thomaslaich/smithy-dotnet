using System.Net.Http;
using System.Runtime.CompilerServices;
using Example.Chat;
using NSmithy.Protocols.Grpc;

var (user, endpoint) = ParseArgs(args, "http://localhost:5002");

using var client = new ChatServiceClient(
    new Uri(endpoint),
    new() { Protocol = new GrpcProtocol() }
);

Console.WriteLine($"Connected to {endpoint} as {user}.");
Console.WriteLine("Type a message and press Enter. Submit an empty line to exit.");
Console.WriteLine();

using var cts = new CancellationTokenSource();
var exiting = false;
try
{
    var output = await client
        .ChatAsync(new ChatInput(ReadConsoleEvents(user, () => exiting = true, cts)), cts.Token)
        .ConfigureAwait(false);
    await foreach (var item in (output.Events ?? EmptyEvents()).WithCancellation(cts.Token))
    {
        if (item is ChatEvent.Message message)
            Console.WriteLine($"{message.Value.User}: {message.Value.Text}");
    }
}
catch (OperationCanceledException) when (exiting || cts.IsCancellationRequested) { }
catch (HttpProtocolException) when (exiting || cts.IsCancellationRequested) { }
catch (IOException) when (exiting || cts.IsCancellationRequested) { }
catch (HttpProtocolException ex)
{
    Console.WriteLine($"Disconnected unexpectedly: {ex.Message}");
}
catch (IOException ex)
{
    Console.WriteLine($"Disconnected unexpectedly: {ex.Message}");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"Could not connect to {endpoint}: {ex.Message}");
}

Console.WriteLine("Disconnected.");

static async IAsyncEnumerable<ChatEvent> EmptyEvents(
    [EnumeratorCancellation] CancellationToken cancellationToken = default
)
{
    await Task.CompletedTask.ConfigureAwait(false);
    yield break;
}

static async IAsyncEnumerable<ChatEvent> ReadConsoleEvents(
    string user,
    Action exiting,
    CancellationTokenSource cancellationTokenSource,
    [EnumeratorCancellation] CancellationToken cancellationToken = default
)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var line = await Task.Run(Console.ReadLine, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            exiting();
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            yield break;
        }

        yield return ChatEvent.FromMessage(new MessageEvent(User: user, Text: line));
    }
}

static (string User, string Endpoint) ParseArgs(string[] args, string defaultEndpoint)
{
    var user = Environment.UserName;
    var endpoint = defaultEndpoint;

    if (args.Length == 1)
    {
        if (LooksLikeEndpoint(args[0]))
            endpoint = NormalizeEndpoint(args[0]);
        else
            user = args[0];
    }
    else if (args.Length >= 2)
    {
        if (LooksLikeEndpoint(args[0]))
        {
            endpoint = NormalizeEndpoint(args[0]);
            user = args[1];
        }
        else
        {
            user = args[0];
            endpoint = NormalizeEndpoint(args[1]);
        }
    }

    return (user, endpoint);
}

static bool LooksLikeEndpoint(string value) =>
    value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
    || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    || int.TryParse(value, out _);

static string NormalizeEndpoint(string value) =>
    int.TryParse(value, out var port) ? $"http://localhost:{port}" : value;

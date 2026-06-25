using System.Net.Http;
using System.Runtime.CompilerServices;
using Example.Chat;
using NSmithy.Protocols.Grpc;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5002";
var user = args.Length > 1 ? args[1] : Environment.UserName;

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
    await foreach (
        var item in client.ChatAsync(ReadConsoleEvents(user, () => exiting = true, cts), cts.Token)
    )
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

Console.WriteLine("Disconnected.");

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

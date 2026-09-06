using Example.Chat.Grpc;
using Grpc.Core;
using Grpc.Net.Client;

var (user, endpoint) = ParseArgs(args, "http://localhost:5002");

using var channel = GrpcChannel.ForAddress(endpoint);
var client = new ChatService.ChatServiceClient(channel);

Console.WriteLine($"Connected to {endpoint} as {user} with Grpc.Net.");
Console.WriteLine("Type a message and press Enter. Submit an empty line to exit.");
Console.WriteLine();

using var cts = new CancellationTokenSource();
var exiting = false;

try
{
    using var call = client.Chat(cancellationToken: cts.Token);
    var writer = WriteConsoleEventsAsync(call.RequestStream, user, () => exiting = true, cts);

    await foreach (var item in call.ResponseStream.ReadAllAsync(cts.Token))
    {
        if (item.ValueCase == ChatEvent.ValueOneofCase.Message)
            Console.WriteLine($"{item.Message.User}: {item.Message.Text}");
    }

    await writer.ConfigureAwait(false);
}
catch (OperationCanceledException) when (exiting || cts.IsCancellationRequested) { }
catch (RpcException) when (exiting || cts.IsCancellationRequested) { }
catch (IOException) when (exiting || cts.IsCancellationRequested) { }
catch (RpcException ex)
{
    Console.WriteLine($"Disconnected unexpectedly: {ex.Status.Detail}");
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

static async Task WriteConsoleEventsAsync(
    IClientStreamWriter<ChatEvent> stream,
    string user,
    Action exiting,
    CancellationTokenSource cancellationTokenSource
)
{
    while (!cancellationTokenSource.IsCancellationRequested)
    {
        var line = await Task.Run(Console.ReadLine, cancellationTokenSource.Token)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(line))
        {
            exiting();
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            return;
        }

        await stream
            .WriteAsync(
                new ChatEvent
                {
                    Message = new MessageEvent { User = user, Text = line },
                },
                cancellationTokenSource.Token
            )
            .ConfigureAwait(false);
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

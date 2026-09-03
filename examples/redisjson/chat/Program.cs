using Examples.Redis.Chat;
using StackExchange.Redis;

var redis = args.Length > 0 ? args[0] : "localhost:6379";
var roomId = args.Length > 1 ? args[1] : "lobby";
var userId = args.Length > 2 ? args[2] : null;
if (string.IsNullOrWhiteSpace(userId))
{
    Console.Write($"Display name [{Environment.UserName}]: ");
    var enteredName = await Console.In.ReadLineAsync();
    userId = string.IsNullOrWhiteSpace(enteredName) ? Environment.UserName : enteredName.Trim();
}

await using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
var chat = new ChatRoomRedisStreams(connection);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var owner = new ChatRoomRedisStreamsConsumer(
    connection,
    new ChatOwner(chat),
    "redis-chat-owner",
    $"example-{Environment.ProcessId}"
);
var owning = owner.RunAsync(cancellationToken: cancellation.Token);
var reading = ReadMessagesAsync(chat, roomId, userId, cancellation.Token);

Console.WriteLine($"Connected to room '{roomId}' as '{userId}'. Type a message; Ctrl+C stops.");
try
{
    while (!cancellation.IsCancellationRequested)
    {
        Console.Write("> ");
        var body = await Console.In.ReadLineAsync(cancellation.Token);
        if (body is null)
            break;
        if (string.IsNullOrWhiteSpace(body))
            continue;

        await chat.PostMessageAsync(
            new PostMessageInput(Body: body, RoomId: roomId, UserId: userId),
            cancellation.Token
        );
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
finally
{
    cancellation.Cancel();
    await IgnoreCancellationAsync(owning);
    await IgnoreCancellationAsync(reading);
}

static async Task ReadMessagesAsync(
    ChatRoomRedisStreams chat,
    string roomId,
    string userId,
    CancellationToken cancellationToken
)
{
    await foreach (
        var item in chat.ReadMessagesAsync(position: "$", cancellationToken: cancellationToken)
    )
    {
        item.Match(
            message =>
            {
                if (message.RoomId != roomId)
                    return 0;

                var sender = message.UserId == userId ? "you" : message.UserId;
                Console.WriteLine($"\n[{sender}] {message.Body}");
                Console.Write("> ");
                return 0;
            },
            (_, _) => 0
        );
    }
}

static async Task IgnoreCancellationAsync(Task task)
{
    try
    {
        await task;
    }
    catch (OperationCanceledException) { }
}

sealed class ChatOwner(ChatRoomRedisStreams chat) : IChatRoomRedisStreamsHandler
{
    public async Task HandlePostMessageAsync(
        PostMessageInput command,
        CancellationToken cancellationToken = default
    )
    {
        await chat.PublishMessagePostedAsync(
            new MessagePosted(
                Body: command.Body,
                RoomId: command.RoomId,
                SentAt: DateTimeOffset.UtcNow,
                UserId: command.UserId
            ),
            cancellationToken
        );
    }
}

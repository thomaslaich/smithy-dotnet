using Examples.Redis.Chat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSmithy.Messaging.Redis;
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
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton(new ChatSession(roomId, userId));
builder.Services.AddRedisStreamsMessaging(connection);
builder.Services.AddChatRoomClient();
builder.Services.AddChatRoomEventPublisher();
builder.Services.AddChatRoomCommandConsumer(
    new RedisStreamConsumerOptions
    {
        ConsumerGroup = "redis-chat-owner",
        ConsumerName = $"example-{Environment.ProcessId}",
    }
);

// Every terminal sees all new events. This demo keeps its XREAD cursor in memory;
// configure CheckpointStore and a stable CheckpointName to resume across restarts.
builder.Services.AddChatRoomEventConsumer(
    new RedisStreamConsumerOptions
    {
        ReadMode = RedisStreamReadMode.Independent,
        StartPosition = "$",
    }
);
builder.Services.AddScoped<IPostMessageHandler, ChatOwner>();
builder.Services.AddScoped<IReadMessagesHandler, ChatReader>();
builder.Services.AddHostedService<ConsoleInput>();

Console.WriteLine($"Connected to room '{roomId}' as '{userId}'. Type a message; Ctrl+C stops.");
await builder.Build().RunAsync();

sealed record ChatSession(string RoomId, string UserId);

sealed class ConsoleInput(
    IChatRoomClient client,
    ChatSession session,
    IHostApplicationLifetime lifetime
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Write("> ");
            // Console input can block even through ReadLineAsync. Cancel the wait so host
            // shutdown does not depend on another keypress; the reader thread owns no services.
            var body = await Task.Run(static () => Console.ReadLine(), CancellationToken.None)
                .WaitAsync(stoppingToken);
            if (body is null)
                break;
            if (string.IsNullOrWhiteSpace(body))
                continue;
            await client.PostMessageAsync(
                new PostMessageInput(Body: body, RoomId: session.RoomId, UserId: session.UserId),
                stoppingToken
            );
        }
        lifetime.StopApplication();
    }
}

sealed class ChatReader(ChatSession session) : IReadMessagesHandler
{
    public Task HandleAsync(ChatEvents message, CancellationToken cancellationToken = default)
    {
        if (message is ChatEvents.MessagePosted posted && posted.Value.RoomId == session.RoomId)
        {
            var sender = posted.Value.UserId == session.UserId ? "you" : posted.Value.UserId;
            Console.WriteLine($"\n[{sender}] {posted.Value.Body}");
            Console.Write("> ");
        }
        return Task.CompletedTask;
    }
}

sealed class ChatOwner(IChatRoomEventPublisher publisher) : IPostMessageHandler
{
    public Task HandleAsync(
        PostMessageInput command,
        CancellationToken cancellationToken = default
    ) =>
        publisher.PublishMessagePostedAsync(
            new MessagePosted(
                Body: command.Body,
                RoomId: command.RoomId,
                SentAt: DateTimeOffset.UtcNow,
                UserId: command.UserId
            ),
            cancellationToken
        );
}

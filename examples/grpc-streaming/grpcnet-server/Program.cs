using System.Collections.Concurrent;
using System.Threading.Channels;
using Example.Chat.Grpc;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 5002;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MinRequestBodyDataRate = null;
    options.ListenLocalhost(
        port,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        }
    );
});
builder.Services.AddGrpc();
builder.Services.AddSingleton<ChatRooms>();

var app = builder.Build();
app.MapGrpcService<GrpcNetChatService>();
app.Run();

internal sealed class GrpcNetChatService(ChatRooms rooms) : ChatService.ChatServiceBase
{
    public override async Task WatchRoom(
        WatchRoomInput request,
        IServerStreamWriter<ChatEvent> responseStream,
        ServerCallContext context
    )
    {
        for (var i = 1; i <= 3; i++)
        {
            await Task.Delay(25, context.CancellationToken).ConfigureAwait(false);
            await responseStream
                .WriteAsync(
                    ChatEvents.FromMessage("server", $"{request.Room}: update {i}"),
                    context.CancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    public override async Task<UploadTranscriptOutput> UploadTranscript(
        IAsyncStreamReader<ChatEvent> requestStream,
        ServerCallContext context
    )
    {
        var accepted = 0;
        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (item.ValueCase == ChatEvent.ValueOneofCase.Message)
                accepted++;
        }

        return new UploadTranscriptOutput { Accepted = (uint)accepted };
    }

    public override async Task Chat(
        IAsyncStreamReader<ChatEvent> requestStream,
        IServerStreamWriter<ChatEvent> responseStream,
        ServerCallContext context
    )
    {
        var fallbackUser = $"user-{Guid.NewGuid():N}"[..13];
        var room = rooms.Join("general");
        var inbound = Task.Run(
            () => PublishIncomingMessagesAsync(requestStream, room, fallbackUser, context),
            context.CancellationToken
        );

        try
        {
            room.Publish(ChatEvents.FromMessage("server", $"{fallbackUser} joined"));
            await foreach (var message in room.ReadAllAsync(context.CancellationToken))
            {
                await responseStream
                    .WriteAsync(message, context.CancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Normal client disconnect.
        }
        finally
        {
            room.Publish(ChatEvents.FromMessage("server", $"{fallbackUser} left"));
            room.Leave();
            await WaitForInboundAsync(inbound).ConfigureAwait(false);
        }
    }

    private static async Task PublishIncomingMessagesAsync(
        IAsyncStreamReader<ChatEvent> requestStream,
        ChatRoomSubscription room,
        string fallbackUser,
        ServerCallContext context
    )
    {
        await foreach (var item in requestStream.ReadAllAsync(context.CancellationToken))
        {
            if (item.ValueCase == ChatEvent.ValueOneofCase.Message)
            {
                var text = item.Message.Text.Trim();
                if (text.Length > 0)
                {
                    var user = string.IsNullOrWhiteSpace(item.Message.User)
                        ? fallbackUser
                        : item.Message.User;
                    room.Publish(ChatEvents.FromMessage(user, text), includeSender: false);
                }
            }
        }
    }

    private static async Task WaitForInboundAsync(Task inbound)
    {
        try
        {
            await inbound.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (RpcException) { }
    }
}

internal sealed class ChatRooms
{
    private readonly ConcurrentDictionary<string, ChatRoom> rooms = [];

    public ChatRoomSubscription Join(string name)
    {
        var room = rooms.GetOrAdd(name, static _ => new ChatRoom());
        return room.Subscribe();
    }
}

internal sealed class ChatRoom
{
    private readonly ConcurrentDictionary<Guid, Channel<ChatEvent>> subscribers = [];

    public ChatRoomSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<ChatEvent>(
            new UnboundedChannelOptions { SingleReader = true }
        );
        subscribers[id] = channel;
        return new ChatRoomSubscription(
            message => Publish(id, message, includeSender: true),
            (message, includeSender) => Publish(id, message, includeSender),
            () => Unsubscribe(id),
            channel.Reader
        );
    }

    private void Publish(Guid senderId, ChatEvent message, bool includeSender)
    {
        foreach (var (id, subscriber) in subscribers)
        {
            if (!includeSender && id == senderId)
                continue;

            subscriber.Writer.TryWrite(message);
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (subscribers.TryRemove(id, out var channel))
            channel.Writer.TryComplete();
    }
}

internal sealed class ChatRoomSubscription(
    Action<ChatEvent> publish,
    Action<ChatEvent, bool> publishWithOptions,
    Action leave,
    ChannelReader<ChatEvent> reader
) : IAsyncDisposable
{
    public void Publish(ChatEvent message) => publish(message);

    public void Publish(ChatEvent message, bool includeSender) =>
        publishWithOptions(message, includeSender);

    public void Leave() => leave();

    public IAsyncEnumerable<ChatEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        leave();
        return ValueTask.CompletedTask;
    }
}

internal static class ChatEvents
{
    public static ChatEvent FromMessage(string user, string text) =>
        new()
        {
            Message = new MessageEvent { User = user, Text = text },
        };
}

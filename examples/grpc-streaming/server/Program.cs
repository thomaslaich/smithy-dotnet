using System.Collections.Concurrent;
using System.Threading.Channels;
using Example.Chat;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var port = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 5002;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(
        port,
        listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        }
    );
});
builder.Services.AddSingleton<ChatRooms>();
builder.Services.AddChatServiceHandler<ChatHandler>();

var app = builder.Build();
app.MapChatServiceGrpc();
app.Run();

internal sealed class ChatHandler(ChatRooms rooms) : IChatServiceHandler
{
    public async IAsyncEnumerable<ChatEvent> WatchRoomAsync(
        WatchRoomInput input,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        for (var i = 1; i <= 3; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(25, cancellationToken);
            yield return ChatEvent.FromMessage(
                new MessageEvent(User: "server", Text: $"{input.Room}: update {i}")
            );
        }
    }

    public async Task<UploadTranscriptOutput> UploadTranscriptAsync(
        IAsyncEnumerable<ChatEvent> input,
        CancellationToken cancellationToken = default
    )
    {
        var accepted = 0;
        await foreach (var item in input.WithCancellation(cancellationToken))
        {
            if (item is ChatEvent.Message)
                accepted++;
        }

        return new UploadTranscriptOutput(accepted);
    }

    public async IAsyncEnumerable<ChatEvent> ChatAsync(
        IAsyncEnumerable<ChatEvent> input,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        var fallbackUser = $"user-{Guid.NewGuid():N}"[..13];
        var room = rooms.Join("general");
        var inbound = Task.Run(
            () => PublishIncomingMessagesAsync(input, room, fallbackUser, cancellationToken),
            cancellationToken
        );

        try
        {
            room.Publish(new MessageEvent(User: "server", Text: $"{fallbackUser} joined"));
            await foreach (var message in room.ReadAllAsync(cancellationToken))
            {
                yield return ChatEvent.FromMessage(message);
            }
        }
        finally
        {
            room.Publish(new MessageEvent(User: "server", Text: $"{fallbackUser} left"));
            room.Leave();
            await WaitForInboundAsync(inbound).ConfigureAwait(false);
        }
    }

    private static async Task PublishIncomingMessagesAsync(
        IAsyncEnumerable<ChatEvent> input,
        ChatRoomSubscription room,
        string fallbackUser,
        CancellationToken cancellationToken
    )
    {
        await foreach (var item in input.WithCancellation(cancellationToken))
        {
            if (item is ChatEvent.Message message)
            {
                var text = message.Value.Text.Trim();
                if (text.Length > 0)
                {
                    room.Publish(
                        new MessageEvent(User: message.Value.User ?? fallbackUser, Text: text),
                        includeSender: false
                    );
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
    private readonly ConcurrentDictionary<Guid, Channel<MessageEvent>> subscribers = [];

    public ChatRoomSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<MessageEvent>(
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

    private void Publish(Guid senderId, MessageEvent message, bool includeSender)
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
    Action<MessageEvent> publish,
    Action<MessageEvent, bool> publishWithOptions,
    Action leave,
    ChannelReader<MessageEvent> reader
) : IAsyncDisposable
{
    public void Publish(MessageEvent message) => publish(message);

    public void Publish(MessageEvent message, bool includeSender) =>
        publishWithOptions(message, includeSender);

    public void Leave() => leave();

    public IAsyncEnumerable<MessageEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        leave();
        return ValueTask.CompletedTask;
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSmithy.Core.Serde;
using NSmithy.Messaging;
using E = Tests.Envelope;
using H = Tests.Header;
using N = Tests.None;

namespace Messaging.Tests;

public sealed class GeneratedKafkaTests
{
    [Fact]
    public async Task CommandClientPreservesOperationKeyAndHeaderOnlyBindings()
    {
        var sender = new CapturingSender();
        var client = new E.DeviceClient(sender);
        using var cancellation = new CancellationTokenSource();
        await client.DimAsync(
            new E.DimInput(DeviceId: "light-1", TraceId: "trace-1"),
            cancellation.Token
        );
        Assert.Equal("tests.envelope#Dim", sender.OperationId);
        Assert.Equal("envelope.commands", sender.Address);
        Assert.Equal(cancellation.Token, sender.CancellationToken);
        var payload = Assert.IsType<MessagePayload>(sender.Payload);
        Assert.Equal("light-1", payload.Key);
        Assert.Equal("trace-1", Encoding.UTF8.GetString(payload.Headers!["trace-id"]));
        using var json = JsonDocument.Parse(payload.Value);
        Assert.False(json.RootElement.TryGetProperty("traceId", out _));
        Assert.Equal("light-1", json.RootElement.GetProperty("deviceId").GetString());

        var handler = new CommandHandler();
        await using var services = new ServiceCollection()
            .AddSingleton<E.IDimHandler>(handler)
            .BuildServiceProvider();
        await new MessageProcessor(
            services.GetRequiredService<IServiceScopeFactory>()
        ).ProcessAsync(E.DeviceMessaging.DimReceive, payload);
        Assert.Equal(new E.DimInput(DeviceId: "light-1", TraceId: "trace-1"), handler.Message);
    }

    [Theory]
    [InlineData("ENVELOPE")]
    [InlineData("HEADER")]
    [InlineData("NONE")]
    public async Task EventPublisherRoundTripsIntoOperationUnion(string mode)
    {
        var sender = new CapturingSender();
        var handler = new EventHandler();
        var collection = new ServiceCollection();
        MessageReceiveBinding receive;
        switch (mode)
        {
            case "ENVELOPE":
                await new E.DeviceEventPublisher(sender).PublishMeasuredAsync(
                    new E.Measured(Lumens: 42, Source: "sensor")
                );
                receive = E.DeviceMessaging.WatchReceive;
                collection.AddSingleton<E.IWatchHandler>(handler);
                break;
            case "HEADER":
                await new H.DeviceEventPublisher(sender).PublishMeasuredAsync(
                    new H.Measured(Lumens: 42, Source: "sensor")
                );
                receive = H.DeviceMessaging.WatchReceive;
                collection.AddSingleton<H.IWatchHandler>(handler);
                break;
            default:
                await new N.DeviceEventPublisher(sender).PublishMeasuredAsync(
                    new N.Measured(Lumens: 42, Source: "sensor")
                );
                receive = N.DeviceMessaging.WatchReceive;
                collection.AddSingleton<N.IWatchHandler>(handler);
                break;
        }
        var payload = Assert.IsType<MessagePayload>(sender.Payload);
        using var json = JsonDocument.Parse(payload.Value);
        var body =
            mode == "ENVELOPE" ? json.RootElement.GetProperty("measured-event") : json.RootElement;
        Assert.Equal(42, body.GetProperty("lumens").GetInt32());
        Assert.False(body.TryGetProperty("source", out _));
        Assert.Equal(mode == "HEADER", payload.Headers!.ContainsKey("bote-type"));
        if (mode == "HEADER")
            Assert.Equal("measured", Encoding.UTF8.GetString(payload.Headers["bote-type"]));
        await using var services = collection.BuildServiceProvider();
        await new MessageProcessor(
            services.GetRequiredService<IServiceScopeFactory>()
        ).ProcessAsync(receive, payload);
        Assert.Equal("sensor", handler.Source);
        Assert.Equal(42, handler.Lumens);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"future-event\":{}}")]
    [InlineData("{\"measured-event\":{},\"other\":{}}")]
    public async Task InvalidEnvelopeFailsBeforeHandlerInvocation(string json)
    {
        var handler = new EventHandler();
        await using var services = new ServiceCollection()
            .AddSingleton<E.IWatchHandler>(handler)
            .BuildServiceProvider();
        await Assert.ThrowsAsync<JsonException>(() =>
            E.DeviceMessaging.WatchReceive.DispatchAsync(
                new MessagePayload(Encoding.UTF8.GetBytes(json)),
                services,
                CancellationToken.None
            )
        );
        Assert.Null(handler.Source);
    }

    [Fact]
    public async Task MissingDiscriminatorAndRequiredHeaderFailBeforeHandlerInvocation()
    {
        var handler = new EventHandler();
        await using var services = new ServiceCollection()
            .AddSingleton<H.IWatchHandler>(handler)
            .BuildServiceProvider();
        await Assert.ThrowsAsync<JsonException>(() =>
            H.DeviceMessaging.WatchReceive.DispatchAsync(
                new MessagePayload("{\"lumens\":42}"u8.ToArray()),
                services,
                CancellationToken.None
            )
        );
        await Assert.ThrowsAsync<MissingRequiredMemberException>(() =>
            H.DeviceMessaging.WatchReceive.DispatchAsync(
                new MessagePayload(
                    "{\"lumens\":42}"u8.ToArray(),
                    Headers: new Dictionary<string, byte[]>
                    {
                        ["bote-type"] = "measured"u8.ToArray(),
                    }
                ),
                services,
                CancellationToken.None
            )
        );
        Assert.Null(handler.Source);
    }

    private sealed class CapturingSender : IMessageSender
    {
        public MessagePayload? Payload { get; private set; }
        public string? OperationId { get; private set; }
        public string? Address { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task SendAsync<T>(
            MessageSendBinding<T> binding,
            T message,
            CancellationToken cancellationToken = default
        )
        {
            Payload = binding.Encode(message);
            OperationId = binding.OperationId;
            Address = binding.Address;
            CancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class CommandHandler : E.IDimHandler
    {
        public E.DimInput? Message { get; private set; }

        public Task HandleAsync(E.DimInput message, CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class EventHandler : E.IWatchHandler, H.IWatchHandler, N.IWatchHandler
    {
        public string? Source { get; private set; }
        public int Lumens { get; private set; }

        public Task HandleAsync(
            E.DeviceEvents message,
            CancellationToken cancellationToken = default
        )
        {
            var value = Assert.IsType<E.DeviceEvents.Measured>(message).Value;
            Source = value.Source;
            Lumens = value.Lumens;
            return Task.CompletedTask;
        }

        public Task HandleAsync(
            H.DeviceEvents message,
            CancellationToken cancellationToken = default
        )
        {
            var value = Assert.IsType<H.DeviceEvents.Measured>(message).Value;
            Source = value.Source;
            Lumens = value.Lumens;
            return Task.CompletedTask;
        }

        public Task HandleAsync(
            N.DeviceEvents message,
            CancellationToken cancellationToken = default
        )
        {
            var value = Assert.IsType<N.DeviceEvents.Measured>(message).Value;
            Source = value.Source;
            Lumens = value.Lumens;
            return Task.CompletedTask;
        }
    }
}

using System.Text;
using NSmithy.EventStream;

namespace NSmithy.Tests.EventStream;

public sealed class EventStreamEventsTests
{
    private static EventStreamMessage Message(params (string Name, string Value)[] headers) =>
        new(
            headers.ToDictionary(
                header => header.Name,
                header => (EventStreamHeaderValue)new EventStreamHeaderValue.Text(header.Value)
            ),
            Encoding.UTF8.GetBytes("{}")
        );

    [Fact]
    public void RoundTripsAnEventPayload()
    {
        var message = EventStreamMessage.Decode(
            EventStreamEvents
                .Create("chunk", Encoding.UTF8.GetBytes("{}"), "application/json")
                .Encode()
        );

        Assert.Equal("chunk", message.StringHeader(EventStreamHeaders.EventType));
        Assert.Equal(
            "{}",
            Encoding.UTF8.GetString(EventStreamEvents.ReadPayload(message, "application/json").Span)
        );
    }

    [Fact]
    public void AcceptsContentTypeParameters()
    {
        var message = Message(
            (EventStreamHeaders.MessageType, EventStreamHeaders.EventMessageType),
            (EventStreamHeaders.ContentType, "application/json; charset=utf-8")
        );

        Assert.Equal(2, EventStreamEvents.ReadPayload(message, "application/json").Length);
    }

    [Fact]
    public void RejectsAnotherMediaTypeWithTheSamePrefix()
    {
        var message = Message(
            (EventStreamHeaders.MessageType, EventStreamHeaders.EventMessageType),
            (EventStreamHeaders.ContentType, "application/cbor-seq")
        );

        Assert.Throws<InvalidDataException>(() =>
            EventStreamEvents.ReadPayload(message, "application/cbor")
        );
    }

    [Fact]
    public void SurfacesAnErrorMessage()
    {
        var message = Message(
            (EventStreamHeaders.MessageType, EventStreamHeaders.ErrorMessageType),
            (EventStreamHeaders.ErrorCode, "Throttled"),
            (EventStreamHeaders.ErrorMessage, "slow down")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EventStreamEvents.EnsureEvent(message)
        );
        Assert.Equal("Throttled: slow down", exception.Message);
    }

    [Fact]
    public void SurfacesAnExceptionMessage()
    {
        var message = Message(
            (EventStreamHeaders.MessageType, EventStreamHeaders.ExceptionMessageType),
            (EventStreamHeaders.ExceptionType, "ValidationError")
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EventStreamEvents.EnsureEvent(message)
        );
        Assert.Contains("ValidationError", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUnknownMessageType()
    {
        var message = Message((EventStreamHeaders.MessageType, "bogus"));

        Assert.Throws<InvalidDataException>(() => EventStreamEvents.EnsureEvent(message));
    }
}

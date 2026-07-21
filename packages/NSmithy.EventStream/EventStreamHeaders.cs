namespace NSmithy.EventStream;

/// <summary>
/// The well-known <c>vnd.amazon.eventstream</c> header names and <c>:message-type</c> values
/// Smithy event-stream protocols use.
/// </summary>
public static class EventStreamHeaders
{
    public const string MessageType = ":message-type";

    public const string EventType = ":event-type";

    public const string ExceptionType = ":exception-type";

    public const string ContentType = ":content-type";

    public const string ErrorCode = ":error-code";

    public const string ErrorMessage = ":error-message";

    /// <summary>A modeled event (<c>:event-type</c> names the event union member).</summary>
    public const string EventMessageType = "event";

    /// <summary>A modeled error (<c>:exception-type</c> names the error shape).</summary>
    public const string ExceptionMessageType = "exception";

    /// <summary>An unmodeled terminal error (<c>:error-code</c>/<c>:error-message</c>).</summary>
    public const string ErrorMessageType = "error";
}

namespace NSmithy.Http;

public abstract record SmithyHttpBody
{
    private SmithyHttpBody() { }

    public static SmithyHttpBody Empty { get; } = new EmptyBody();

    public sealed record Bytes(byte[] Content) : SmithyHttpBody
    {
        public byte[] Content { get; } =
            Content ?? throw new ArgumentNullException(nameof(Content));
    }

    public sealed record Streaming(Stream Content, long? ContentLength = null) : SmithyHttpBody
    {
        public Stream Content { get; } =
            Content ?? throw new ArgumentNullException(nameof(Content));
    }

    /// <summary>
    /// An event-stream body: protocol-framed message chunks, each written and flushed as one unit
    /// to preserve message boundaries on the wire. Carries an input or duplex event stream's
    /// request body; the protocol owns all framing, so the transport writes chunks and nothing more.
    /// </summary>
    public sealed record EventStreaming(IAsyncEnumerable<ReadOnlyMemory<byte>> Content)
        : SmithyHttpBody
    {
        public IAsyncEnumerable<ReadOnlyMemory<byte>> Content { get; } =
            Content ?? throw new ArgumentNullException(nameof(Content));
    }

    private sealed record EmptyBody : SmithyHttpBody;
}

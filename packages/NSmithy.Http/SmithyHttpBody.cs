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

    private sealed record EmptyBody : SmithyHttpBody;
}

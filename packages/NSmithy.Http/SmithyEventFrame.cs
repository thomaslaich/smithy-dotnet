namespace NSmithy.Http;

/// <summary>
/// A single logical event/message in a streaming operation.
/// </summary>
public sealed class SmithyEventFrame
{
    public SmithyEventFrame(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload;
    }

    public SmithyEventFrame(
        byte[] payload,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers
    )
        : this(payload)
    {
        Headers = headers ?? Headers;
    }

    /// <summary>The protocol-encoded event payload, excluding transport framing.</summary>
    public byte[] Payload { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}

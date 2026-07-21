namespace NSmithy.EventStream;

/// <summary>
/// A typed <c>vnd.amazon.eventstream</c> header value. The wire format tags every header with
/// one of ten value types; this union mirrors them one-to-one.
/// </summary>
public abstract record EventStreamHeaderValue
{
    private EventStreamHeaderValue() { }

    public sealed record Bool(bool Value) : EventStreamHeaderValue;

    public sealed record Signed8(sbyte Value) : EventStreamHeaderValue;

    public sealed record Signed16(short Value) : EventStreamHeaderValue;

    public sealed record Signed32(int Value) : EventStreamHeaderValue;

    public sealed record Signed64(long Value) : EventStreamHeaderValue;

    public sealed record Blob : EventStreamHeaderValue
    {
        public Blob(byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Performance",
            "CA1819:Properties should not return arrays",
            Justification = "Wire bytes; ownership passes to the message."
        )]
        public byte[] Value { get; }

        public bool Equals(Blob? other) =>
            other is not null && Value.AsSpan().SequenceEqual(other.Value);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.AddBytes(Value);
            return hash.ToHashCode();
        }
    }

    public sealed record Text : EventStreamHeaderValue
    {
        public Text(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public string Value { get; }
    }

    /// <summary>Millisecond-precision UTC timestamp.</summary>
    public sealed record Timestamp(DateTimeOffset Value) : EventStreamHeaderValue;

    public sealed record Uuid(Guid Value) : EventStreamHeaderValue;
}

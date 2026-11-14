namespace NSmithy.Core.Serde;

/// <summary>
/// How closely a codec holds the wire to the model when reading.
/// </summary>
/// <remarks>
/// A server reads what a caller sent and owes a structured 400 for anything the model does not
/// allow, so it reads <see cref="Strict"/>. A client reads a peer it does not control — a real
/// service that predates a rule, or is simply looser than the spec — and refusing a response it can
/// understand helps nobody, so it reads <see cref="Lenient"/>. Two rules differ: the UTC offset on
/// a <c>date-time</c> timestamp, which Smithy's own protocol tests require a server to reject and a
/// client to accept, and a required member nobody set, which a server rejects and a client reads
/// as its zero value (Smithy's client error correction; see
/// <see cref="StructSchema{T, TBuilder}.CompleteMembers"/>). Codec factories read
/// <see cref="Strict"/> unless told otherwise, so a reader that does not say which side it is on
/// keeps the server's rules.
/// </remarks>
public enum WireReadMode
{
    /// <summary>Accept what can be understood. What a client uses.</summary>
    Lenient,

    /// <summary>Accept only what the model declares. What a server uses, and the default.</summary>
    Strict,
}

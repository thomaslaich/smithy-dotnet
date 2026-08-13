namespace NSmithy.Core.Serde;

/// <summary>
/// How closely a codec holds the wire to the model when reading.
/// </summary>
/// <remarks>
/// A server reads what a caller sent and owes a structured 400 for anything the model does not
/// allow, so it reads <see cref="Strict"/>. A client reads a peer it does not control — a real
/// service that predates a rule, or is simply looser than the spec — and refusing a response it can
/// understand helps nobody, so it reads <see cref="Lenient"/>. Only one rule currently differs: the
/// UTC offset on a <c>date-time</c> timestamp, which Smithy's own protocol tests require a server to
/// reject and a client to accept.
/// </remarks>
public enum WireReadMode
{
    /// <summary>Accept what can be understood. The default, and what a client uses.</summary>
    Lenient,

    /// <summary>Accept only what the model declares. What a server uses.</summary>
    Strict,
}

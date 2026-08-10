namespace NSmithy.Core.Serde;

/// <summary>
/// Thrown by a codec when a required member is absent from a payload. It is distinct from a plain
/// <see cref="InvalidOperationException"/> because the two directions mean different things: on a
/// server it is a caller mistake that the runtime turns into the modeled
/// <c>smithy.framework#ValidationException</c>, while on a client it is a broken peer and stays an
/// exception.
/// </summary>
public sealed class MissingRequiredMemberException(string memberName)
    : Exception($"Missing required member '{memberName}'.")
{
    private readonly List<string> pathTokens = [memberName];

    public string MemberName { get; } = memberName;

    /// <summary>
    /// Tokens from the payload root down to the missing member, outermost first. The reader that
    /// detects the omission only knows the member's own name, so each enclosing reader prepends its
    /// own step as the exception unwinds — which is what lets the member be reported where it sits
    /// rather than by name alone.
    /// </summary>
    public IReadOnlyList<string> PathTokens => pathTokens;

    public void PrependPathToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        pathTokens.Insert(0, token);
    }
}

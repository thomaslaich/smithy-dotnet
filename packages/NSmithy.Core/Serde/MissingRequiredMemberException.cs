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
    public string MemberName { get; } = memberName;
}

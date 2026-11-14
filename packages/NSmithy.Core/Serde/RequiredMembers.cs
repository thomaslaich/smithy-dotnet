namespace NSmithy.Core.Serde;

/// <summary>
/// A structure's required members, numbered in declaration order. A member's number (its
/// ordinal) is the presence bit its builder sets for it; see <see cref="StructBuilder"/>.
/// </summary>
internal sealed class RequiredMembers
{
    // The member index of each ordinal, and the ordinal of each member index (-1 when optional).
    private readonly int[] indices;
    private readonly int[] ordinals;

    public RequiredMembers(ShapeId id, MemberSchema[] members, Type builderType)
    {
        indices = [.. Enumerable.Range(0, members.Length).Where(i => members[i].IsRequired)];
        ordinals = new int[members.Length];
        Array.Fill(ordinals, -1);
        for (var ordinal = 0; ordinal < indices.Length; ordinal++)
        {
            ordinals[indices[ordinal]] = ordinal;
        }

        // Builders only implement the presence bits their structure needs.
        var builderBase =
            indices.Length > 64 ? typeof(LargeStructBuilder)
            : indices.Length > 0 ? typeof(StructBuilder)
            : null;
        if (builderBase is not null && !builderBase.IsAssignableFrom(builderType))
        {
            throw new InvalidOperationException(
                $"Structure schema '{id}' has {indices.Length} required members, so its builder "
                    + $"'{builderType.Name}' must derive from {builderBase.Name}."
            );
        }
    }

    public int Count => indices.Length;

    /// <summary>The ordinal of the member at <paramref name="index"/>, or -1 when optional.</summary>
    public int OrdinalOf(int index) => ordinals[index];

    /// <summary>The member index of the required member with <paramref name="ordinal"/>.</summary>
    public int IndexOf(int ordinal) => indices[ordinal];
}

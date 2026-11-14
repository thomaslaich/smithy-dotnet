namespace NSmithy.Core.Serde;

/// <summary>
/// The base of every structure builder: it records which members a reader has set, so
/// <see cref="StructSchema{T, TBuilder}.CompleteMembers"/> can tell a required member the payload
/// left out from one it sent, whatever the format. A member's builder property cannot tell: it
/// starts at the member's modeled default, and a value-type member at zero.
/// </summary>
public abstract class StructBuilder
{
    // Members 0-63; a structure with more keeps the rest in one bit array.
    private ulong low;
    private ulong[]? high;

    internal void MarkSet(int index)
    {
        if (index < 64)
        {
            low |= 1UL << index;
            return;
        }

        var word = (index >> 6) - 1;
        if (high is null || high.Length <= word)
        {
            Array.Resize(ref high, word + 1);
        }

        high[word] |= 1UL << (index & 63);
    }

    internal bool IsSet(int index)
    {
        if (index < 64)
        {
            return (low & (1UL << index)) != 0;
        }

        var word = (index >> 6) - 1;
        return high is not null && word < high.Length && (high[word] & (1UL << (index & 63))) != 0;
    }
}

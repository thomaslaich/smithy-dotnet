namespace NSmithy.Core.Serde;

/// <summary>
/// The base of every structure builder: it records which required members a reader has set, so
/// <see cref="StructSchema{T, TBuilder}.CompleteMembers"/> can tell a required member the payload
/// left out from one it sent, whatever the format. A member's builder property cannot tell: it
/// starts at the member's modeled default, and a value-type member at zero. One bit per required
/// member, numbered in declaration order; a structure with more than 64 derives from
/// <see cref="LargeStructBuilder"/>.
/// </summary>
public abstract class StructBuilder
{
    private ulong present;

    internal void MarkSet(int ordinal)
    {
        if (ordinal < 64)
        {
            present |= 1UL << ordinal;
        }
        else
        {
            ((LargeStructBuilder)this).MarkSetPast64(ordinal);
        }
    }

    internal bool IsSet(int ordinal) =>
        ordinal < 64
            ? (present & (1UL << ordinal)) != 0
            : ((LargeStructBuilder)this).IsSetPast64(ordinal);
}

/// <summary>
/// The builder base of a structure with more than 64 required members, which keeps the bits past
/// the first 64 in an array.
/// </summary>
public abstract class LargeStructBuilder : StructBuilder
{
    private ulong[]? more;

    internal void MarkSetPast64(int ordinal)
    {
        var word = (ordinal >> 6) - 1;
        if (more is null || more.Length <= word)
        {
            Array.Resize(ref more, word + 1);
        }

        more[word] |= 1UL << (ordinal & 63);
    }

    internal bool IsSetPast64(int ordinal)
    {
        var word = (ordinal >> 6) - 1;
        return more is not null
            && word < more.Length
            && (more[word] & (1UL << (ordinal & 63))) != 0;
    }
}

namespace RpcV2.Conformance;

/// <summary>
/// Picks the generated type for a Smithy shape when several fixture services generate types of
/// the same name into one assembly, such as an <c>EmptyInputOutput</c> operation in both the
/// protocol service and the corpus service.
/// </summary>
internal static class GeneratedTypes
{
    /// <summary>
    /// Whether a type was generated from the namespace of <paramref name="shapeId"/>. C#
    /// namespaces are the Smithy namespace in Pascal case, behind a projection's
    /// <c>baseNamespace</c>, so dots and case are ignored.
    /// </summary>
    public static bool BelongsTo(Type type, string shapeId)
    {
        var smithyNamespace = shapeId.Split('#')[0].Replace(".", "", StringComparison.Ordinal);
        var csharpNamespace = (type.Namespace ?? "").Replace(".", "", StringComparison.Ordinal);
        return csharpNamespace.EndsWith(smithyNamespace, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The only candidate, or the only one from the namespace of <paramref name="shapeId"/>.
    /// </summary>
    public static T Pick<T>(
        IReadOnlyList<T> candidates,
        Func<T, Type> typeOf,
        string shapeId,
        string what
    )
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var matching = candidates.Where(c => BelongsTo(typeOf(c), shapeId)).ToList();
        return matching.Count == 1
            ? matching[0]
            : throw new InvalidOperationException(
                $"Expected one generated {what} for {shapeId} but found {candidates.Count}, "
                    + $"{matching.Count} of them in its namespace."
            );
    }
}

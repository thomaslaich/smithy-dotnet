namespace NSmithy.Core.Serde;

/// <summary>
/// A placeholder a compiler registers for a shape before compiling it, so a schema that references
/// itself resolves to the in-progress result instead of recursing forever.
/// </summary>
public interface IDeferredCompilation<in TCompiled>
{
    void Complete(TCompiled compiled);
}

/// <summary>
/// Memoizes one compiled artifact per shape for the lifetime of a single compile pass.
/// <para>
/// The key is <see cref="Schema.Resolved"/>, never the schema as handed in: a lazy schema stands in
/// for its target and forwards everything to it, so two references to the same shape are different
/// objects. Resolving here rather than at each call site is the point — a compiler that forgot
/// would silently compile the same shape twice and lose the cycle protection along with it.
/// </para>
/// </summary>
public sealed class SchemaCompilationCache
{
    private readonly Dictionary<Schema, object> entries = new(ReferenceEqualityComparer.Instance);

    public TCompiled GetOrCompile<TCompiled, TDeferred>(
        Schema schema,
        Func<TDeferred> createPlaceholder,
        Func<Schema, TCompiled> compile
    )
        where TDeferred : TCompiled, IDeferredCompilation<TCompiled>
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(createPlaceholder);
        ArgumentNullException.ThrowIfNull(compile);

        var resolved = schema.Resolved;
        if (entries.TryGetValue(resolved, out var cached))
        {
            return (TCompiled)cached;
        }

        var placeholder = createPlaceholder();
        entries.Add(resolved, placeholder!);
        placeholder.Complete(compile(resolved));
        return placeholder;
    }
}

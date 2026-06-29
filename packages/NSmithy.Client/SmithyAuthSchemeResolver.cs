using NSmithy.Core.Serde;

namespace NSmithy.Client;

/// <summary>
/// Resolves client auth, selecting at most one auth scheme for the runtime interceptor pipeline.
///
/// Selection follows Smithy's effective-auth-scheme order: <paramref name="modeledAuthSchemes"/>
/// is the priority-ordered list of auth scheme ids the service models (computed at codegen time
/// from the <c>@auth</c> trait, defaulting to all of the service's auth traits in alphabetical
/// order). The first modeled scheme for which the caller supplied a matching
/// <see cref="ISmithyAuthScheme"/> wins — exactly one signer is installed, mirroring smithy-java's
/// "first supported scheme with an available identity" rule.
/// </summary>
public static class SmithyAuthSchemeResolver
{
    public static IReadOnlyList<IClientInterceptor> Resolve(
        Uri endpoint,
        ServiceSchema service,
        IReadOnlyList<string> modeledAuthSchemes,
        IEnumerable<ISmithyAuthScheme>? authSchemes,
        IEnumerable<IClientInterceptor>? interceptors = null
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(modeledAuthSchemes);

        var resolved = new List<IClientInterceptor>();
        if (interceptors is not null)
        {
            resolved.AddRange(interceptors);
        }

        var configured = authSchemes?.ToList();
        if (configured is null || configured.Count == 0)
        {
            // No auth configured: send anonymously. Services that require auth will reject the call.
            return resolved;
        }

        foreach (var schemeId in modeledAuthSchemes)
        {
            var match = configured.FirstOrDefault(s =>
                string.Equals(s.SchemeId, schemeId, StringComparison.Ordinal)
            );
            if (match is not null)
            {
                resolved.Add(
                    match.CreateInterceptor(new SmithyAuthSchemeContext(endpoint, service))
                );
                return resolved;
            }
        }

        throw new InvalidOperationException(
            $"None of the configured auth schemes [{string.Join(", ", configured.Select(s => s.SchemeId))}] "
                + $"match the auth schemes modeled by service '{service.Id}' "
                + $"[{string.Join(", ", modeledAuthSchemes)}]. Configure a scheme the service supports, "
                + "or omit auth schemes for anonymous access."
        );
    }
}

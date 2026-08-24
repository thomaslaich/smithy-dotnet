using NSmithy.Core.Serde;

namespace NSmithy.Client;

/// <summary>
/// Resolves client auth. At construction it indexes configured <see cref="ISmithyAuthScheme"/>
/// instances by scheme id and fails fast when none of them are modeled by the service. Per
/// invocation the runtime then selects the
/// first scheme of the operation's effective modeled list (a per-operation <c>@auth</c> trait
/// overrides the service default) that is configured, optionally narrowed by the
/// resolved endpoint. Identity resolution and signing happen after selection, once per attempt.
/// </summary>
public static class SmithyAuthSchemeResolver
{
    /// <summary>
    /// Indexes the configured auth schemes by shape id. Returns an empty map when no schemes are
    /// configured (anonymous access).
    /// </summary>
    public static IReadOnlyDictionary<string, ISmithyAuthScheme> ResolveSchemes(
        ServiceSchema service,
        IReadOnlyList<string> modeledAuthSchemes,
        IEnumerable<ISmithyAuthScheme>? authSchemes
    )
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(modeledAuthSchemes);

        var configured = authSchemes?.ToList();
        if (configured is null || configured.Count == 0)
        {
            // No auth configured: send anonymously. Services that require auth will reject the
            // call.
            return new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal);
        }

        if (!configured.Any(s => modeledAuthSchemes.Contains(s.SchemeId, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"None of the configured auth schemes [{string.Join(", ", configured.Select(s => s.SchemeId))}] "
                    + $"match the auth schemes modeled by service '{service.Id}' "
                    + $"[{string.Join(", ", modeledAuthSchemes)}]. Configure a scheme the service supports, "
                    + "or omit auth schemes for anonymous access."
            );
        }

        var resolved = new Dictionary<string, ISmithyAuthScheme>(StringComparer.Ordinal);
        foreach (var scheme in configured)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scheme.SchemeId);
            ArgumentNullException.ThrowIfNull(scheme.IdentityResolver);
            ArgumentNullException.ThrowIfNull(scheme.Signer);
            if (!resolved.TryAdd(scheme.SchemeId, scheme))
            {
                throw new InvalidOperationException(
                    $"Auth scheme '{scheme.SchemeId}' was configured more than once."
                );
            }
        }

        return resolved;
    }

    /// <summary>
    /// Selects the auth scheme for one invocation: the first of the operation's modeled scheme
    /// ids — narrowed to <paramref name="endpointAuthSchemes"/> when the resolved endpoint
    /// provided one — that is configured. Returns null for anonymous operations
    /// or when nothing is configured; throws when the operation models auth, schemes are
    /// configured, but none match.
    /// </summary>
    public static ISmithyAuthScheme? SelectScheme(
        IReadOnlyList<string> operationAuthSchemes,
        IReadOnlyList<string>? endpointAuthSchemes,
        IReadOnlyDictionary<string, ISmithyAuthScheme> schemes
    )
    {
        ArgumentNullException.ThrowIfNull(operationAuthSchemes);
        ArgumentNullException.ThrowIfNull(schemes);

        if (operationAuthSchemes.Count == 0 || schemes.Count == 0)
        {
            return null;
        }

        var sawCandidate = false;
        foreach (var schemeId in operationAuthSchemes)
        {
            if (
                endpointAuthSchemes is not null
                && !endpointAuthSchemes.Contains(schemeId, StringComparer.Ordinal)
            )
            {
                continue;
            }

            sawCandidate = true;
            if (schemes.TryGetValue(schemeId, out var scheme))
            {
                return scheme;
            }
        }

        if (!sawCandidate)
        {
            // The endpoint narrowed away every modeled scheme; treat as anonymous.
            return null;
        }

        throw new InvalidOperationException(
            $"None of the configured auth schemes [{string.Join(", ", schemes.Keys)}] match "
                + $"the operation's modeled auth schemes [{string.Join(", ", operationAuthSchemes)}]"
                + (
                    endpointAuthSchemes is null
                        ? "."
                        : $" narrowed by the endpoint to [{string.Join(", ", endpointAuthSchemes)}]."
                )
        );
    }
}

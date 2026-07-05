using NSmithy.Core.Serde;

namespace NSmithy.Client;

/// <summary>
/// Resolves client auth. At construction it creates one interceptor per configured
/// <see cref="ISmithyAuthScheme"/>, keyed by scheme id, and fails fast when none of the
/// configured schemes are modeled by the service. Per invocation the runtime then selects the
/// first scheme of the operation's effective modeled list (a per-operation <c>@auth</c> trait
/// overrides the service default) that has a configured interceptor, optionally narrowed by the
/// resolved endpoint — mirroring smithy-java's "first supported scheme with an available
/// identity" rule.
/// </summary>
public static class SmithyAuthSchemeResolver
{
    /// <summary>
    /// Creates the configured auth interceptors, keyed by auth scheme shape id. Returns an
    /// empty map when no schemes are configured (anonymous access).
    /// </summary>
    public static IReadOnlyDictionary<string, IClientInterceptor> ResolveInterceptors(
        Uri endpoint,
        ServiceSchema service,
        IReadOnlyList<string> serviceAuthSchemes,
        IEnumerable<ISmithyAuthScheme>? authSchemes
    )
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(serviceAuthSchemes);

        var configured = authSchemes?.ToList();
        if (configured is null || configured.Count == 0)
        {
            // No auth configured: send anonymously. Services that require auth will reject the
            // call.
            return new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal);
        }

        if (!configured.Any(s => serviceAuthSchemes.Contains(s.SchemeId, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"None of the configured auth schemes [{string.Join(", ", configured.Select(s => s.SchemeId))}] "
                    + $"match the auth schemes modeled by service '{service.Id}' "
                    + $"[{string.Join(", ", serviceAuthSchemes)}]. Configure a scheme the service supports, "
                    + "or omit auth schemes for anonymous access."
            );
        }

        var context = new SmithyAuthSchemeContext(endpoint, service);
        var interceptors = new Dictionary<string, IClientInterceptor>(StringComparer.Ordinal);
        foreach (var scheme in configured)
        {
            interceptors[scheme.SchemeId] = scheme.CreateInterceptor(context);
        }

        return interceptors;
    }

    /// <summary>
    /// Selects the interceptor for one invocation: the first of the operation's modeled scheme
    /// ids — narrowed to <paramref name="endpointAuthSchemes"/> when the resolved endpoint
    /// provided one — that has a configured interceptor. Returns null for anonymous operations
    /// or when nothing is configured; throws when the operation models auth, schemes are
    /// configured, but none match.
    /// </summary>
    public static IClientInterceptor? SelectInterceptor(
        IReadOnlyList<string> operationAuthSchemes,
        IReadOnlyList<string>? endpointAuthSchemes,
        IReadOnlyDictionary<string, IClientInterceptor> interceptors
    )
    {
        ArgumentNullException.ThrowIfNull(operationAuthSchemes);
        ArgumentNullException.ThrowIfNull(interceptors);

        if (operationAuthSchemes.Count == 0 || interceptors.Count == 0)
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
            if (interceptors.TryGetValue(schemeId, out var interceptor))
            {
                return interceptor;
            }
        }

        if (!sawCandidate)
        {
            // The endpoint narrowed away every modeled scheme; treat as anonymous.
            return null;
        }

        throw new InvalidOperationException(
            $"None of the configured auth schemes [{string.Join(", ", interceptors.Keys)}] match "
                + $"the operation's modeled auth schemes [{string.Join(", ", operationAuthSchemes)}]"
                + (
                    endpointAuthSchemes is null
                        ? "."
                        : $" narrowed by the endpoint to [{string.Join(", ", endpointAuthSchemes)}]."
                )
        );
    }
}

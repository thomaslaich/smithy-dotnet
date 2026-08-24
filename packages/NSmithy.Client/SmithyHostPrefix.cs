using System.Text;

namespace NSmithy.Client;

/// <summary>A modeled value substituted into an operation's endpoint host prefix.</summary>
public readonly record struct SmithyHostLabel(string Name, string? Value);

/// <summary>Expands modeled <c>@endpoint(hostPrefix)</c> and <c>@hostLabel</c> traits.</summary>
public static class SmithyHostPrefix
{
    public static string Expand(string template, params ReadOnlySpan<SmithyHostLabel> labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        var expanded = new StringBuilder(template);
        foreach (var label in labels)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label.Name);
            if (string.IsNullOrEmpty(label.Value))
            {
                throw new ArgumentException(
                    $"Host label '{label.Name}' must not be null or empty.",
                    nameof(labels)
                );
            }

            expanded.Replace($"{{{label.Name}}}", label.Value);
        }

        if (expanded.ToString().Contains('{', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Host prefix '{template}' contains an unresolved label.",
                nameof(labels)
            );
        }

        return expanded.ToString();
    }

    internal static SmithyEndpoint Apply(SmithyEndpoint endpoint, string hostPrefix)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPrefix);

        var builder = new UriBuilder(endpoint.Uri) { Host = hostPrefix + endpoint.Uri.Host };
        return new SmithyEndpoint(builder.Uri, endpoint.Headers, endpoint.AuthSchemes);
    }
}

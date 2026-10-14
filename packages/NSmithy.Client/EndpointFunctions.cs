namespace NSmithy.Client;

/// <summary>
/// The Smithy rules engine standard library, called by generated endpoint resolvers. Functions
/// that return an optional value return null for "not set".
/// </summary>
public static class EndpointFunctions
{
    /// <summary>
    /// <c>parseURL</c>: splits an <c>http</c> or <c>https</c> URL into its parts. Returns null for
    /// other schemes, malformed URLs, and URLs with a query string.
    /// </summary>
    public static EndpointUrl? ParseUrl(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            return null;
        }
        var scheme = value[..schemeEnd];
        if (
            !string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = value.Length;
        }
        var authority = value[authorityStart..authorityEnd];
        if (authority.Length == 0 || !HasValidPort(authority))
        {
            return null;
        }

        var rest = value[authorityEnd..];
        var fragment = rest.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            rest = rest[..fragment];
        }
        if (rest.Contains('?', StringComparison.Ordinal))
        {
            return null;
        }

        return new EndpointUrl(
            scheme.ToLowerInvariant(),
            authority,
            rest,
            NormalizePath(rest),
            IsIpAddress(Host(authority))
        );
    }

    /// <summary>
    /// <c>substring</c>: the characters from <paramref name="start"/> up to
    /// <paramref name="stop"/>, counted from the end when <paramref name="reverse"/> is true.
    /// Returns null when the range does not fit or the value contains non-ASCII characters.
    /// </summary>
    public static string? Substring(string value, int start, int stop, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (start >= stop || value.Length < stop || !System.Text.Ascii.IsValid(value))
        {
            return null;
        }
        return reverse ? value[(value.Length - stop)..(value.Length - start)] : value[start..stop];
    }

    /// <summary>
    /// <c>uriEncode</c>: percent-encodes everything except the RFC 3986 unreserved characters.
    /// </summary>
    public static string UriEncode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Uri.EscapeDataString(value);
    }

    /// <summary>
    /// <c>isValidHostLabel</c>: whether <paramref name="value"/> is a DNS host label (letters,
    /// digits, and hyphens, not starting with a hyphen, at most 63 characters), or a dot-separated
    /// sequence of them when <paramref name="allowSubDomains"/> is true.
    /// </summary>
    public static bool IsValidHostLabel(string value, bool allowSubDomains)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!allowSubDomains)
        {
            return IsValidSingleLabel(value);
        }
        foreach (var label in value.Split('.'))
        {
            if (!IsValidSingleLabel(label))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// <c>split</c>: splits on a non-empty delimiter. A <paramref name="limit"/> of 0 splits on
    /// every occurrence; otherwise the result has at most <paramref name="limit"/> parts, the last
    /// holding the unsplit remainder.
    /// </summary>
    public static IReadOnlyList<string> Split(string value, string delimiter, int limit)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(delimiter);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        if (value.Length == 0 || limit == 1)
        {
            return [value];
        }
        return value.Split(delimiter, limit == 0 ? int.MaxValue : limit, StringSplitOptions.None);
    }

    /// <summary>
    /// <c>getAttr</c> list indexing: the element at <paramref name="index"/>, counted from the end
    /// when negative, or null when it is out of range.
    /// </summary>
    public static string? Index(IReadOnlyList<string>? values, int index)
    {
        if (values is null)
        {
            return null;
        }
        var position = index < 0 ? values.Count + index : index;
        return position >= 0 && position < values.Count ? values[position] : null;
    }

    private static bool IsValidSingleLabel(string label)
    {
        if (label.Length is < 1 or > 63 || !char.IsAsciiLetterOrDigit(label[0]))
        {
            return false;
        }
        foreach (var c in label.AsSpan(1))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasValidPort(string authority)
    {
        var hostStart = authority.LastIndexOf('@') + 1;
        var portSeparator = authority.LastIndexOf(':');
        var ipv6End = authority.LastIndexOf(']');
        if (portSeparator < hostStart || portSeparator < ipv6End)
        {
            return true;
        }
        var port = authority.AsSpan(portSeparator + 1);
        foreach (var c in port)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    private static string Host(string authority)
    {
        var host = authority[(authority.LastIndexOf('@') + 1)..];
        if (host.StartsWith('['))
        {
            var end = host.IndexOf(']', StringComparison.Ordinal);
            return end < 0 ? host : host[..(end + 1)];
        }
        var port = host.LastIndexOf(':');
        return port < 0 ? host : host[..port];
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }
        var start = path.StartsWith('/') ? string.Empty : "/";
        var end = path.EndsWith('/') ? string.Empty : "/";
        return start + path + end;
    }

    // The rules engine's heuristic: a bracketed IPv6 literal, or four dot-separated groups of one
    // to three digits without leading zeros. Ranges are deliberately not checked.
    private static bool IsIpAddress(string host)
    {
        if (host.Length < 2)
        {
            return false;
        }
        if (host[0] == '[' && host[^1] == ']')
        {
            return true;
        }

        var groups = host.Split('.');
        if (groups.Length != 4)
        {
            return false;
        }
        foreach (var group in groups)
        {
            if (
                group.Length is < 1 or > 3
                || (group.Length > 1 && group[0] == '0')
                || !group.All(char.IsAsciiDigit)
            )
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>The parts of a URL parsed by the rules engine's <c>parseURL</c>.</summary>
/// <param name="Scheme">The scheme, without <c>://</c>.</param>
/// <param name="Authority">The authority: host, plus port and user info when present.</param>
/// <param name="Path">The path as written, possibly empty.</param>
/// <param name="NormalizedPath">The path with a leading and trailing slash.</param>
/// <param name="IsIp">Whether the host is an IPv4 or bracketed IPv6 literal.</param>
public sealed record EndpointUrl(
    string Scheme,
    string Authority,
    string Path,
    string NormalizedPath,
    bool IsIp
);

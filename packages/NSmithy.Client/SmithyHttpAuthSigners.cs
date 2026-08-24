using NSmithy.Http;

namespace NSmithy.Client;

internal sealed class HeaderAuthSigner(string headerName, string? prefix = null) : ISmithySigner
{
    private readonly string headerName = string.IsNullOrWhiteSpace(headerName)
        ? throw new ArgumentException("Header name must be set.", nameof(headerName))
        : headerName;

    public ValueTask<SmithyHttpRequest> SignAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        ISmithyIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var value = IdentityValue(identity);
        request.Headers[headerName] = [prefix is null ? value : $"{prefix} {value}"];
        return ValueTask.FromResult(request);
    }

    internal static string IdentityValue(ISmithyIdentity identity) =>
        identity is SmithyTokenIdentity tokenIdentity
            ? tokenIdentity.Value
            : throw new ArgumentException(
                $"Expected a {nameof(SmithyTokenIdentity)} but received {identity.GetType().Name}.",
                nameof(identity)
            );
}

internal sealed class QueryParameterAuthSigner(string name) : ISmithySigner
{
    private readonly string name = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Query parameter name must be set.", nameof(name))
        : name;

    public ValueTask<SmithyHttpRequest> SignAsync(
        SmithyContext context,
        SmithyHttpRequest request,
        ISmithyIdentity identity,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var separator = request.RequestUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        request.RequestUri =
            $"{request.RequestUri}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(HeaderAuthSigner.IdentityValue(identity))}";
        return ValueTask.FromResult(request);
    }
}

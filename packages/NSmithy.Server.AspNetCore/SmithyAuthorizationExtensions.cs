using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NSmithy.Server.AspNetCore;

/// <summary>Authorizes a mapped Smithy service's operations by their modeled auth.</summary>
public static class SmithyAuthorizationExtensions
{
    /// <summary>
    /// Requires authorization for the operations whose model requires authentication, and allows
    /// anonymous calls to operations with <c>@optionalAuth</c> or no auth scheme, overriding
    /// authorization required by an enclosing group or a fallback policy. Protected operations
    /// are authorized with <paramref name="policyNames"/>, or the default policy without any.
    /// </summary>
    /// <remarks>
    /// An endpoint that serves both protected and anonymous operations, as with protocols that
    /// resolve the operation from the request, authorizes after it resolves the operation.
    /// </remarks>
    public static SmithyServiceEndpointConventionBuilder WithModeledAuthorization(
        this SmithyServiceEndpointConventionBuilder builder,
        params string[] policyNames
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(policyNames);

        IAuthorizeData[] authorizeData =
            policyNames.Length == 0
                ? [new AuthorizeAttribute()]
                : [.. policyNames.Select(policy => new AuthorizeAttribute(policy))];
        builder.Add(endpoint =>
        {
            if (
                endpoint.Metadata.OfType<SmithyEndpointMetadata>().LastOrDefault() is not { } smithy
            )
            {
                return;
            }

            var operations = smithy.Operations;
            var protectedCount = operations.Count(operation => operation.RequiresAuthentication);
            if (protectedCount > 0 && protectedCount == operations.Count)
            {
                foreach (var data in authorizeData)
                {
                    endpoint.Metadata.Add(data);
                }

                return;
            }

            endpoint.Metadata.Add(new AllowAnonymousAttribute());
            if (protectedCount > 0)
            {
                endpoint.Metadata.Add(new SmithyOperationAuthorization(authorizeData));
            }
        });
        return builder;
    }
}

/// <summary>
/// Authorizes protected operations of an endpoint that also serves anonymous ones, once the
/// operation is known, the way the authorization middleware authorizes an endpoint.
/// </summary>
internal sealed class SmithyOperationAuthorization(IReadOnlyList<IAuthorizeData> authorizeData)
{
    public async Task AuthorizeAsync(HttpContext httpContext, RequestDelegate next)
    {
        var services = httpContext.RequestServices;
        var policy =
            await AuthorizationPolicy
                .CombineAsync(
                    services.GetRequiredService<IAuthorizationPolicyProvider>(),
                    authorizeData
                )
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Authorization requires a policy.");
        var evaluator = services.GetRequiredService<IPolicyEvaluator>();
        var authentication = await evaluator
            .AuthenticateAsync(policy, httpContext)
            .ConfigureAwait(false);
        var authorization = await evaluator
            .AuthorizeAsync(policy, authentication, httpContext, httpContext)
            .ConfigureAwait(false);
        await services
            .GetRequiredService<IAuthorizationMiddlewareResultHandler>()
            .HandleAsync(next, httpContext, policy, authorization)
            .ConfigureAwait(false);
    }
}

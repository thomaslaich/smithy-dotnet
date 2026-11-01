using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
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
    /// are authorized with <paramref name="policyNames"/>, or the default policy without any,
    /// together with authorization requirements attached to the endpoint or enclosing groups.
    /// </summary>
    /// <remarks>
    /// This convention does not map modeled auth scheme IDs to ASP.NET Core authentication
    /// schemes. Configure authentication handlers and policies to select accepted credentials.
    /// Endpoints without <see cref="SmithyEndpointMetadata"/> are left alone. An endpoint that
    /// serves both protected and anonymous operations, as with protocols that resolve the
    /// operation from the request, authorizes after it resolves the operation.
    /// </remarks>
    public static TBuilder WithModeledAuthorization<TBuilder>(
        this TBuilder builder,
        params string[] policyNames
    )
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(policyNames);

        IAuthorizeData[] authorizeData =
            policyNames.Length == 0
                ? [new AuthorizeAttribute()]
                : [.. policyNames.Select(policy => new AuthorizeAttribute(policy))];
        // A route group runs its conventions before each endpoint's own, which add the
        // SmithyEndpointMetadata this reads; finally conventions run after them.
        builder.Finally(endpoint =>
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
        // Read the completed endpoint so enclosing and later conventions participate, just as
        // they do for a route endpoint. Ignore the AllowAnonymous marker used to defer this check.
        var metadata = httpContext.GetEndpoint()!.Metadata;
        var policy =
            await AuthorizationPolicy
                .CombineAsync(
                    services.GetRequiredService<IAuthorizationPolicyProvider>(),
                    metadata.GetOrderedMetadata<IAuthorizeData>().Concat(authorizeData),
                    metadata.GetOrderedMetadata<AuthorizationPolicy>()
                )
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Authorization requires a policy.");
        var requirements = metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
        if (requirements.Count > 0)
        {
            var builder = new AuthorizationPolicyBuilder(policy);
            foreach (var requirement in requirements)
            {
                builder.AddRequirements([.. requirement.GetRequirements()]);
            }

            policy = builder.Build();
        }

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

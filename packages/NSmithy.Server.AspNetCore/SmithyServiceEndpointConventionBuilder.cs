using Microsoft.AspNetCore.Builder;

namespace NSmithy.Server.AspNetCore;

/// <summary>
/// Applies conventions to every endpoint of a mapped Smithy service. Each endpoint carries
/// <see cref="SmithyEndpointMetadata"/>, so a convention can tell operations apart.
/// </summary>
public sealed class SmithyServiceEndpointConventionBuilder : IEndpointConventionBuilder
{
    private readonly IReadOnlyList<IEndpointConventionBuilder> endpoints;

    internal SmithyServiceEndpointConventionBuilder(
        IReadOnlyList<IEndpointConventionBuilder> endpoints
    ) => this.endpoints = endpoints;

    public void Add(Action<EndpointBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        foreach (var endpoint in endpoints)
        {
            endpoint.Add(convention);
        }
    }

    public void Finally(Action<EndpointBuilder> finallyConvention)
    {
        ArgumentNullException.ThrowIfNull(finallyConvention);
        foreach (var endpoint in endpoints)
        {
            endpoint.Finally(finallyConvention);
        }
    }
}

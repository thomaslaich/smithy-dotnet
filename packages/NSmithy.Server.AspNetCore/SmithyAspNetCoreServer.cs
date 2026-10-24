using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NSmithy.Core;
using NSmithy.Core.Serde;
using NSmithy.Http;

namespace NSmithy.Server.AspNetCore;

/// <summary>A protocol to map a service over, with the name used in endpoint names and errors.</summary>
public sealed record SmithyServerProtocol(string Name, IProtocol Protocol);

/// <summary>Maps services to ASP.NET Core endpoints from the routes their protocols report.</summary>
public static partial class SmithyAspNetCoreServer
{
    public const string PackageName = "NSmithy.Server.AspNetCore";

    /// <summary>
    /// Maps the operations of <paramref name="catalog"/> over each of
    /// <paramref name="protocols"/>: one endpoint per route a protocol reports, or one endpoint for
    /// a protocol that resolves the operation from the request. Two routes with the same method and
    /// path fail.
    /// </summary>
    public static IEndpointRouteBuilder MapSmithyService(
        this IEndpointRouteBuilder endpoints,
        ServiceOperationCatalog catalog,
        IReadOnlyList<SmithyServerProtocol> protocols
    )
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(protocols);

        var service = catalog.Schema;
        var operations = catalog.Operations;

        IOperationSchema[] schemas = [.. operations.Select(operation => operation.Schema)];
        var mappedRoutes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var protocol in protocols)
        {
            var serviceProtocol = protocol.Protocol.ForService(service);
            var bound = operations.ToDictionary(
                operation => operation.Schema.Id,
                operation => operation.Bind(serviceProtocol)
            );
            switch (serviceProtocol.GetServerRouting(schemas))
            {
                case ServerRouting.ByRoute byRoute:
                    foreach (var route in byRoute.Routes)
                    {
                        if (!bound.TryGetValue(route.Operation, out var operation))
                        {
                            throw new InvalidOperationException(
                                $"{protocol.Name} routes '{route.Operation}', which {service.Id.Name} does not map."
                            );
                        }

                        var pattern = ToAspNetCoreRoute(route.Path);
                        EnsureRouteAvailable(
                            mappedRoutes,
                            route.Method,
                            pattern,
                            protocol,
                            service
                        );
                        var requiredQuery = route.RequiredQuery;
                        endpoints
                            .MapMethods(
                                pattern,
                                [route.Method],
                                httpContext =>
                                    DispatchRouteAsync(httpContext, operation, requiredQuery)
                            )
                            .WithDisplayName($"{route.Operation.Name} ({protocol.Name})");
                    }

                    break;

                case ServerRouting.ByDispatch byDispatch:
                    // Resolving the operation reads the request, so its body is buffered before
                    // any operation sees it.
                    if (
                        bound.Values.FirstOrDefault(operation => operation.StreamsRequestBody) is
                        { } streaming
                    )
                    {
                        throw new InvalidOperationException(
                            $"{protocol.Name} routes {service.Id.Name} by dispatch, which buffers request bodies, but operation '{streaming.Schema.Id}' streams its request body."
                        );
                    }

                    EnsureRouteAvailable(
                        mappedRoutes,
                        byDispatch.Method,
                        byDispatch.Path,
                        protocol,
                        service
                    );
                    endpoints
                        .MapMethods(
                            byDispatch.Path,
                            [byDispatch.Method],
                            httpContext => DispatchByNameAsync(httpContext, byDispatch, bound)
                        )
                        .WithDisplayName($"{service.Id.Name} ({protocol.Name})");
                    break;
            }
        }

        return endpoints;
    }

    private static async Task DispatchRouteAsync(
        HttpContext httpContext,
        IServerOperationBinding operation,
        IReadOnlyList<KeyValuePair<string, string?>> requiredQuery
    )
    {
        foreach (var (name, value) in requiredQuery)
        {
            if (!SmithyAspNetCoreHost.HasExpectedQueryLiteral(httpContext, name, value))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        await SmithyAspNetCoreHost
            .DispatchAsync(httpContext, operation, httpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task DispatchByNameAsync(
        HttpContext httpContext,
        ServerRouting.ByDispatch routing,
        Dictionary<ShapeId, IServerOperationBinding> operations
    )
    {
        var cancellationToken = httpContext.RequestAborted;
        var request = await SmithyAspNetCoreHost
            .ToSmithyRequestAsync(httpContext, streamBody: false, cancellationToken)
            .ConfigureAwait(false);
        var operationId = await routing
            .ResolveOperation(request, cancellationToken)
            .ConfigureAwait(false);
        if (operationId is { } id && operations.TryGetValue(id, out var operation))
        {
            await SmithyAspNetCoreHost
                .DispatchBuiltRequestAsync(httpContext, operation, request, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SmithyAspNetCoreHost
            .WriteResponseAsync(httpContext, routing.UnknownOperation(request), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureRouteAvailable(
        HashSet<string> mappedRoutes,
        string method,
        string pattern,
        SmithyServerProtocol protocol,
        ServiceSchema service
    )
    {
        var route = method + " " + pattern;
        if (!mappedRoutes.Add(route))
        {
            throw new InvalidOperationException(
                $"Mapping {protocol.Name} for {service.Id.Name} would register duplicate route '{route}'. "
                    + "Map conflicting protocols on different endpoint route builders, hosts, or ports."
            );
        }
    }

    // Converts a route path from Smithy's URI syntax to an ASP.NET Core route template. They
    // differ only in greedy labels: "/files/{key+}" becomes "/files/{**key}", ASP.NET Core's
    // catch-all.
    private static string ToAspNetCoreRoute(string path) => GreedyLabel().Replace(path, "{**$1}");

    // Matches a greedy label such as {key+}, capturing its name.
    [GeneratedRegex(@"\{(\w+)\+\}")]
    private static partial Regex GreedyLabel();
}

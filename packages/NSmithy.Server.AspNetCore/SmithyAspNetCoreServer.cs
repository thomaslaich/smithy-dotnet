using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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
    /// path fail, and so does an operation none of whose <see cref="IServerOperation.HandlerTypes"/>
    /// is registered. The endpoints are mapped into a route group, which is returned so conventions
    /// apply to all of them, and each carries <see cref="SmithyEndpointMetadata"/>.
    /// </summary>
    public static RouteGroupBuilder MapSmithyService(
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
        RequireHandlers(endpoints.ServiceProvider, service, operations);

        IOperationSchema[] schemas = [.. operations.Select(operation => operation.Schema)];
        var mappedRoutes = new HashSet<string>(StringComparer.Ordinal);
        var group = endpoints.MapGroup("");
        foreach (var protocol in protocols)
        {
            var serviceProtocol = protocol.Protocol.ForService(service);
            var bound = catalog.Bind(protocol.Protocol);
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
                        group
                            .MapMethods(
                                pattern,
                                [route.Method],
                                httpContext =>
                                    DispatchRouteAsync(httpContext, operation, requiredQuery)
                            )
                            .WithDisplayName($"{route.Operation.Name} ({protocol.Name})")
                            .WithMetadata(
                                new SmithyEndpointMetadata(
                                    service,
                                    protocol.Name,
                                    [catalog.GetOperation(route.Operation)]
                                )
                            );
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
                    group
                        .MapMethods(
                            byDispatch.Path,
                            [byDispatch.Method],
                            httpContext =>
                                DispatchByNameAsync(httpContext, catalog, byDispatch, bound)
                        )
                        .WithDisplayName($"{service.Id.Name} ({protocol.Name})")
                        .WithMetadata(
                            new SmithyEndpointMetadata(service, protocol.Name, operations)
                        );
                    break;
            }
        }

        return group;
    }

    // Fails at startup rather than on the first call to an operation without a handler.
    private static void RequireHandlers(
        IServiceProvider services,
        ServiceSchema service,
        IReadOnlyList<IServerOperation> operations
    )
    {
        if (services.GetService<IServiceProviderIsService>() is not { } registered)
        {
            return;
        }

        string[] missing =
        [
            .. operations
                .Where(operation =>
                    operation.HandlerTypes.Count > 0
                    && !operation.HandlerTypes.Any(registered.IsService)
                )
                .Select(operation => operation.Schema.Id.Name),
        ];
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"{service.Id.Name} has no handler for {string.Join(", ", missing)}. Register its service handler, or each operation's handler."
            );
        }
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
        ServiceOperationCatalog catalog,
        ServerRouting.ByDispatch routing,
        IReadOnlyDictionary<ShapeId, IServerOperationBinding> operations
    )
    {
        var cancellationToken = httpContext.RequestAborted;
        var startTimestamp = Stopwatch.GetTimestamp();
        var request = await SmithyAspNetCoreHost
            .ToSmithyRequestAsync(httpContext, streamBody: false, cancellationToken)
            .ConfigureAwait(false);
        var operationId = await routing
            .ResolveOperation(request, cancellationToken)
            .ConfigureAwait(false);
        if (operationId is { } id && operations.TryGetValue(id, out var operation))
        {
            if (
                catalog.GetOperation(id).RequiresAuthentication
                && httpContext.GetEndpoint()?.Metadata.GetMetadata<SmithyOperationAuthorization>()
                    is { } authorization
            )
            {
                await authorization
                    .AuthorizeAsync(
                        httpContext,
                        context =>
                            SmithyAspNetCoreHost.DispatchBuiltRequestAsync(
                                context,
                                operation,
                                request,
                                startTimestamp,
                                cancellationToken
                            )
                    )
                    .ConfigureAwait(false);
                return;
            }

            await SmithyAspNetCoreHost
                .DispatchBuiltRequestAsync(
                    httpContext,
                    operation,
                    request,
                    startTimestamp,
                    cancellationToken
                )
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

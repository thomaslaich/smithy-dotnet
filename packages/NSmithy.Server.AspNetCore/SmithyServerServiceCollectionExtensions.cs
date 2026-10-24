using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSmithy.Server;

namespace NSmithy.Server.AspNetCore;

/// <summary>Registers the runtime used by generated ASP.NET Core endpoints.</summary>
public static class SmithyServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default server runtime unless the application already registered one. Mapped
    /// services use a default runtime when none is registered, so this is optional; it gives the
    /// runtime a place in the container for applications that configure or replace it.
    /// </summary>
    public static IServiceCollection AddSmithyServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<SmithyServerRuntime>();
        return services;
    }
}

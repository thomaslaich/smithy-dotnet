using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSmithy.Server;

namespace NSmithy.Server.AspNetCore;

/// <summary>Registers the runtime used by generated ASP.NET Core endpoints.</summary>
public static class SmithyServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default server runtime unless the application already registered one.
    /// Generated aggregate handler registration calls this automatically. Applications that
    /// register operation handlers individually should call this before building the host.
    /// </summary>
    public static IServiceCollection AddSmithyServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<SmithyServerRuntime>();
        return services;
    }
}

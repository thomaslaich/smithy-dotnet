using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using NSmithy.Server;

namespace NSmithy.Server.Mcp;

/// <summary>Registers generated Smithy service operations with an MCP server.</summary>
public static class SmithyMcpServerBuilderExtensions
{
    /// <summary>Adds the unary operations in <paramref name="catalog"/> as MCP tools.</summary>
    public static IMcpServerBuilder WithSmithyTools(
        this IMcpServerBuilder builder,
        ServiceOperationCatalog catalog
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalog);
        return builder.WithTools(SmithyMcpTools.Create(catalog));
    }

    /// <summary>
    /// Adds Smithy operations resolved from dependency injection as MCP tools. The catalog is
    /// created when MCP server options are materialized, after application services are available.
    /// </summary>
    public static IMcpServerBuilder WithSmithyTools(
        this IMcpServerBuilder builder,
        Func<IServiceProvider, ServiceOperationCatalog> catalogFactory
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalogFactory);
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(
            services => new ConfigureOptions<McpServerOptions>(options =>
            {
                var tools = options.ToolCollection ?? [];
                foreach (var tool in SmithyMcpTools.Create(catalogFactory(services)))
                {
                    tools.Add(tool);
                }

                if (!tools.IsEmpty)
                {
                    options.ToolCollection = tools;
                }
            })
        );
        return builder;
    }

    /// <summary>Adds Smithy operations from a handler resolved through dependency injection.</summary>
    public static IMcpServerBuilder WithSmithyTools<THandler>(
        this IMcpServerBuilder builder,
        Func<THandler, ServiceOperationCatalog> catalogFactory
    )
        where THandler : class
    {
        ArgumentNullException.ThrowIfNull(catalogFactory);
        return builder.WithSmithyTools(services =>
            catalogFactory(services.GetRequiredService<THandler>())
        );
    }
}

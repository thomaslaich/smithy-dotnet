using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using NSmithy.Core.Serde;
using NSmithy.Server;

namespace NSmithy.Server.Mcp;

/// <summary>Registers generated Smithy service tools and prompts with an MCP server.</summary>
public static class SmithyMcpServerBuilderExtensions
{
    /// <summary>
    /// Adds the tools and prompts for the generated service definition
    /// <typeparamref name="TDefinition"/>, such as <c>WeatherServiceDefinition</c>. The definition is
    /// resolved from dependency injection when MCP server options are materialized; each tool call
    /// resolves its handler from the call's services.
    /// </summary>
    public static IMcpServerBuilder WithSmithyService<TDefinition>(this IMcpServerBuilder builder)
        where TDefinition : class, IServiceDefinition
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithSmithyDefinition(services =>
            SingleDefinition(
                services.GetServices<IServiceDefinition>().OfType<TDefinition>(),
                typeof(TDefinition).FullName
            )
        );
    }

    /// <summary>
    /// Adds the tools and prompts for the generated service identified by
    /// <paramref name="schema"/>. The generated service definition is resolved from dependency
    /// injection when MCP server options are materialized; each tool call resolves its handler from
    /// the call's services.
    /// </summary>
    public static IMcpServerBuilder WithSmithyService(
        this IMcpServerBuilder builder,
        ServiceSchema schema
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schema);
        return builder.WithSmithyDefinition(services =>
            SingleDefinition(
                services
                    .GetServices<IServiceDefinition>()
                    .Where(definition => definition.Schema.Id == schema.Id),
                schema.Id.ToString()
            )
        );
    }

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

    /// <summary>Adds generated Smithy prompt definitions to an MCP server.</summary>
    public static IMcpServerBuilder WithSmithyPrompts(
        this IMcpServerBuilder builder,
        IEnumerable<ServicePromptDefinition> definitions
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definitions);
        return builder.WithPrompts(SmithyMcpPrompts.Create(definitions));
    }

    private static IServiceDefinition SingleDefinition(
        IEnumerable<IServiceDefinition> definitions,
        string? name
    )
    {
        var matches = definitions.ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Generated service definition '{name}' is not registered. Call the generated "
                    + "Add{Service} method, or Add{Service}Handler for an aggregate handler, "
                    + "before WithSmithyService."
            ),
            _ => throw new InvalidOperationException(
                $"Generated service definition '{name}' is registered more than once."
            ),
        };
    }

    private static IMcpServerBuilder WithSmithyDefinition(
        this IMcpServerBuilder builder,
        Func<IServiceProvider, IServiceDefinition> definitionFactory
    )
    {
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(
            services => new ConfigureOptions<McpServerOptions>(options =>
            {
                var tools = options.ToolCollection ?? [];
                var prompts = options.PromptCollection ?? [];
                var definition = definitionFactory(services);
                foreach (
                    var tool in SmithyMcpTools.Create(definition.Operations)
                )
                {
                    tools.Add(tool);
                }

                foreach (var prompt in SmithyMcpPrompts.Create(definition.Prompts))
                {
                    prompts.Add(prompt);
                }

                if (!tools.IsEmpty)
                {
                    options.ToolCollection = tools;
                }

                if (!prompts.IsEmpty)
                {
                    options.PromptCollection = prompts;
                }
            })
        );
        return builder;
    }
}

using NSmithy.Core.Serde;

namespace NSmithy.Server;

/// <summary>
/// A generated Smithy service surface registered with dependency injection. Host adapters use the
/// definition to discover static service metadata and bind each operation to its independently
/// registered handler.
/// </summary>
public interface IServiceDefinition
{
    ServiceSchema Schema { get; }

    IReadOnlyList<ServicePromptDefinition> Prompts { get; }

    ServiceOperationCatalog CreateOperationCatalog(IServiceProvider services);
}

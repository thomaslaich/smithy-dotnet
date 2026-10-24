using NSmithy.Core.Serde;

namespace NSmithy.Server;

/// <summary>
/// A generated Smithy service surface registered with dependency injection. Host adapters use the
/// definition to find the service's metadata and operation table; each operation resolves its
/// handler from the services of the request it serves.
/// </summary>
public interface IServiceDefinition
{
    ServiceSchema Schema { get; }

    IReadOnlyList<ServicePromptDefinition> Prompts { get; }

    ServiceOperationCatalog Operations { get; }
}

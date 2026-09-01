using System.Collections.ObjectModel;

namespace NSmithy.Server;

/// <summary>Transport-neutral metadata for one argument accepted by a Smithy prompt.</summary>
public sealed class ServicePromptArgumentDefinition
{
    public ServicePromptArgumentDefinition(string name, string? description, bool isRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        IsRequired = isRequired;
    }

    public string Name { get; }

    public string? Description { get; }

    public bool IsRequired { get; }
}

/// <summary>
/// A prompt template declared on a Smithy service or one of its operations. The definition is
/// transport-neutral; adapters decide how to expose and render it.
/// </summary>
public sealed class ServicePromptDefinition
{
    public ServicePromptDefinition(
        string name,
        string description,
        string template,
        string? preferWhen = null,
        IEnumerable<ServicePromptArgumentDefinition>? arguments = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(template);

        var copiedArguments = arguments?.ToArray() ?? [];
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in copiedArguments)
        {
            ArgumentNullException.ThrowIfNull(argument);
            if (!names.Add(argument.Name))
            {
                throw new ArgumentException(
                    $"Prompt '{name}' contains more than one argument named '{argument.Name}'.",
                    nameof(arguments)
                );
            }
        }

        Name = name;
        Description = description;
        Template = template;
        PreferWhen = preferWhen;
        Arguments = new ReadOnlyCollection<ServicePromptArgumentDefinition>(copiedArguments);
    }

    public string Name { get; }

    public string Description { get; }

    public string Template { get; }

    public string? PreferWhen { get; }

    public IReadOnlyList<ServicePromptArgumentDefinition> Arguments { get; }
}

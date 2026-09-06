using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSmithy.Server;

namespace NSmithy.Server.Mcp;

/// <summary>Creates MCP prompts from generated Smithy prompt definitions.</summary>
public static class SmithyMcpPrompts
{
    public static IReadOnlyList<McpServerPrompt> Create(
        IEnumerable<ServicePromptDefinition> definitions
    )
    {
        ArgumentNullException.ThrowIfNull(definitions);
        List<McpServerPrompt> prompts = [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!names.Add(definition.Name))
            {
                throw new InvalidOperationException(
                    $"More than one Smithy prompt maps to MCP prompt name '{definition.Name}' "
                        + "when compared without regard to case."
                );
            }

            prompts.Add(new SmithyMcpPrompt(definition));
        }

        return new ReadOnlyCollection<McpServerPrompt>(prompts);
    }
}

internal sealed class SmithyMcpPrompt : McpServerPrompt
{
    private const string PreferWhenPrefix = "\n\nTool preference: ";

    private readonly ServicePromptDefinition definition;
    private readonly Dictionary<string, ServicePromptArgumentDefinition> argumentsByName;

    public SmithyMcpPrompt(ServicePromptDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        argumentsByName = definition.Arguments.ToDictionary(
            argument => argument.Name,
            StringComparer.Ordinal
        );
        ProtocolPrompt = new Prompt
        {
            Name = definition.Name,
            Description = definition.Description,
            Arguments =
            [
                .. definition.Arguments.Select(argument => new PromptArgument
                {
                    Name = argument.Name,
                    Description = argument.Description,
                    Required = argument.IsRequired,
                }),
            ],
        };
    }

    public override Prompt ProtocolPrompt { get; }

    public override IReadOnlyList<object> Metadata { get; } = [];

    public override ValueTask<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var supplied = request.Params.Arguments;
        if (supplied is not null)
        {
            foreach (var argument in supplied)
            {
                if (!argumentsByName.ContainsKey(argument.Key))
                {
                    throw InvalidParams(
                        $"Prompt '{definition.Name}' does not declare argument '{argument.Key}'."
                    );
                }

                if (argument.Value.ValueKind != JsonValueKind.String)
                {
                    throw InvalidParams(
                        $"Argument '{argument.Key}' for prompt '{definition.Name}' must be a string."
                    );
                }
            }
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in definition.Arguments)
        {
            if (supplied is not null && supplied.TryGetValue(argument.Name, out var value))
            {
                values.Add(argument.Name, value.GetString()!);
            }
            else if (argument.IsRequired)
            {
                throw InvalidParams(
                    $"Prompt '{definition.Name}' requires argument '{argument.Name}'."
                );
            }
            else
            {
                values.Add(argument.Name, string.Empty);
            }
        }

        var template = definition.Template;
        if (!string.IsNullOrEmpty(definition.PreferWhen))
        {
            template += PreferWhenPrefix + definition.PreferWhen;
        }

        return ValueTask.FromResult(
            new GetPromptResult
            {
                Description = definition.Description,
                Messages =
                [
                    new PromptMessage
                    {
                        Role = Role.User,
                        Content = new TextContentBlock { Text = Render(template, values) },
                    },
                ],
            }
        );
    }

    private static string Render(string template, Dictionary<string, string> arguments)
    {
        var result = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var opening = template.IndexOf("{{", position, StringComparison.Ordinal);
            if (opening < 0)
            {
                result.Append(template, position, template.Length - position);
                break;
            }

            result.Append(template, position, opening - position);
            var closing = template.IndexOf("}}", opening + 2, StringComparison.Ordinal);
            if (closing < 0)
            {
                result.Append(template, opening, template.Length - opening);
                break;
            }

            var name = template.Substring(opening + 2, closing - opening - 2);
            if (IsPlaceholderName(name))
            {
                result.Append(arguments.TryGetValue(name, out var value) ? value : string.Empty);
            }
            else
            {
                result.Append(template, opening, closing + 2 - opening);
            }

            position = closing + 2;
        }

        return result.ToString();
    }

    private static bool IsPlaceholderName(string name) =>
        name.Length > 0
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static McpProtocolException InvalidParams(string message) =>
        new(message, McpErrorCode.InvalidParams);
}

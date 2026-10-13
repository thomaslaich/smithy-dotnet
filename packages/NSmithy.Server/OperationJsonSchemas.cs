namespace NSmithy.Server;

/// <summary>
/// Canonical JSON Schema 2020-12 documents and the description generated for an operation's MCP
/// tool projection.
/// </summary>
public sealed class OperationJsonSchemas
{
    public OperationJsonSchemas(string input, string output, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        Input = input;
        Output = output;
        Description = description;
    }

    public string Input { get; }

    public string Output { get; }

    /// <summary>The operation's modeled documentation, if any.</summary>
    public string? Description { get; }
}

namespace NSmithy.Server;

/// <summary>
/// Canonical JSON Schema 2020-12 documents generated for an operation's MCP tool projection.
/// </summary>
public sealed class OperationJsonSchemas
{
    public OperationJsonSchemas(string input, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        Input = input;
        Output = output;
    }

    public string Input { get; }

    public string Output { get; }
}

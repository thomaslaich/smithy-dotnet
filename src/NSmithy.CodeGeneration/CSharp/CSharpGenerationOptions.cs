namespace NSmithy.CodeGeneration.CSharp;

public sealed record CSharpGenerationOptions(
    string? BaseNamespace = null,
    IReadOnlyCollection<string>? GeneratedNamespaces = null
);

namespace Smithy.NET.CodeGeneration.CSharp;

public sealed record CSharpGenerationOptions(
    string? BaseNamespace = null,
    CSharpNullabilityMode NullabilityMode = CSharpNullabilityMode.NonAuthoritative,
    IReadOnlyCollection<string>? GeneratedNamespaces = null
);

public enum CSharpNullabilityMode
{
    NonAuthoritative,
    Authoritative,
}

namespace Smithy.NET.CodeGeneration.CSharp;

public sealed record CSharpGenerationOptions(
    string? BaseNamespace = null,
    CSharpNullabilityMode NullabilityMode = CSharpNullabilityMode.NonAuthoritative
);

public enum CSharpNullabilityMode
{
    NonAuthoritative,
    Authoritative,
}

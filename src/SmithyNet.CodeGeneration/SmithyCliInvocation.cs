namespace SmithyNet.CodeGeneration;

internal sealed record SmithyCliInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory
);

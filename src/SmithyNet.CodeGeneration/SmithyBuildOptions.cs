namespace SmithyNet.CodeGeneration;

public sealed record SmithyBuildOptions(
    string WorkingDirectory,
    string BuildFile,
    string OutputDirectory,
    string? Projection = null,
    string? SmithyCliPath = null
);

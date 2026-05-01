namespace NSmithy.CodeGeneration;

internal sealed record SmithyCliRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
);

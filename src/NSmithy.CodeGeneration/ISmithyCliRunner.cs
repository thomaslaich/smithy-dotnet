namespace NSmithy.CodeGeneration;

internal interface ISmithyCliRunner
{
    ValueTask<SmithyCliRunResult> RunAsync(
        SmithyCliInvocation invocation,
        CancellationToken cancellationToken
    );
}

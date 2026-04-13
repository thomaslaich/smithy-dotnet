namespace Smithy.NET.CodeGeneration;

internal interface ISmithyCliRunner
{
    ValueTask<SmithyCliRunResult> RunAsync(
        SmithyCliInvocation invocation,
        CancellationToken cancellationToken
    );
}

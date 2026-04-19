using System.ComponentModel;
using System.Diagnostics;

namespace SmithyNet.CodeGeneration;

internal sealed class ProcessSmithyCliRunner : ISmithyCliRunner
{
    public async ValueTask<SmithyCliRunResult> RunAsync(
        SmithyCliInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(invocation.WorkingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = invocation.FileName,
                WorkingDirectory = invocation.WorkingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };

        foreach (var argument in invocation.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException)
        {
            throw new SmithyException(
                $"Unable to start Smithy CLI at '{invocation.FileName}'. Verify that Java and the Smithy CLI are installed.",
                exception
            );
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new SmithyCliRunResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false)
        );
    }
}

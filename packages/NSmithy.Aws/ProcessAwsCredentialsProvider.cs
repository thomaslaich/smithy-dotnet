using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NSmithy.Aws;

/// <summary>
/// Runs a <c>credential_process</c> command and reads the credentials it writes to standard output
/// as JSON (<c>Version</c> 1). The command line is split into a program and arguments the way the
/// AWS CLI splits it, and is not run through a shell.
/// </summary>
public sealed class ProcessAwsCredentialsProvider : IAwsCredentialsProvider
{
    private readonly string command;
    private readonly Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<ProcessAwsCredentialsResult>
    > runProcess;

    /// <param name="command">The <c>credential_process</c> command line.</param>
    /// <param name="runProcess">Runs a program with arguments. Default: a child process.</param>
    public ProcessAwsCredentialsProvider(
        string command,
        Func<
            IReadOnlyList<string>,
            CancellationToken,
            Task<ProcessAwsCredentialsResult>
        >? runProcess = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        this.command = command;
        this.runProcess = runProcess ?? RunAsync;
    }

    public async ValueTask<AwsCredentials> GetCredentialsAsync(
        CancellationToken cancellationToken = default
    )
    {
        var arguments = Split(command);
        if (arguments.Count == 0)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                "credential_process is empty."
            );
        }

        ProcessAwsCredentialsResult result;
        try
        {
            result = await runProcess(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                $"credential_process '{arguments[0]}' could not be started.",
                innerException: exception
            );
        }

        if (result.ExitCode != 0)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                $"credential_process '{arguments[0]}' exited with code {result.ExitCode}: {result.StandardError.Trim()}"
            );
        }

        ProcessCredentials? credentials;
        try
        {
            credentials = JsonSerializer.Deserialize(
                result.StandardOutput,
                ProcessCredentialsContext.Default.ProcessCredentials
            );
        }
        catch (JsonException exception)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                $"credential_process '{arguments[0]}' did not write valid JSON.",
                innerException: exception
            );
        }

        if (credentials?.Version != 1)
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                $"credential_process '{arguments[0]}' must write Version 1 credentials."
            );
        }
        if (
            string.IsNullOrWhiteSpace(credentials.AccessKeyId)
            || string.IsNullOrWhiteSpace(credentials.SecretAccessKey)
        )
        {
            throw new AwsCredentialsProviderException(
                nameof(ProcessAwsCredentialsProvider),
                $"credential_process '{arguments[0]}' wrote no AccessKeyId and SecretAccessKey."
            );
        }
        return new AwsCredentials(
            credentials.AccessKeyId,
            credentials.SecretAccessKey,
            credentials.SessionToken,
            credentials.Expiration
        );
    }

    /// <summary>
    /// Splits a command line like a POSIX shell's word splitting: whitespace separates words,
    /// single quotes keep everything literal, double quotes keep whitespace, and a backslash
    /// escapes the next character outside single quotes.
    /// </summary>
    internal static IReadOnlyList<string> Split(string commandLine)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var inWord = false;
        var quote = '\0';
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }
            if (c == '\\' && i + 1 < commandLine.Length)
            {
                current.Append(commandLine[++i]);
                inWord = true;
                continue;
            }
            if (quote == '"')
            {
                if (c == '"')
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }
            if (c is '\'' or '"')
            {
                quote = c;
                inWord = true;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    inWord = false;
                }
                continue;
            }
            current.Append(c);
            inWord = true;
        }
        if (inWord)
        {
            words.Add(current.ToString());
        }
        return words;
    }

    private static async Task<ProcessAwsCredentialsResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo(arguments[0])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(start)
            ?? throw new IOException($"credential_process '{arguments[0]}' did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessAwsCredentialsResult(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false)
        );
    }
}

/// <summary>The exit code and output of a <c>credential_process</c> run.</summary>
public sealed record ProcessAwsCredentialsResult(
    int ExitCode,
    string StandardOutput,
    string StandardError
);

internal sealed record ProcessCredentials(
    int Version,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? SessionToken,
    DateTimeOffset? Expiration
);

[JsonSerializable(typeof(ProcessCredentials))]
internal sealed partial class ProcessCredentialsContext : JsonSerializerContext;

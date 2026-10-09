using Microsoft.Build.Framework;
using MsBuildTask = Microsoft.Build.Utilities.Task;

namespace NSmithy.Contracts;

/// <summary>Resolves the project's pinned CLI, downloading it only during explicit installation.</summary>
public sealed class ResolveSmithyCli : MsBuildTask, ICancelableTask, IDisposable
{
    private readonly CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(10));

    public string? CachePath { get; set; }
    public string? DownloadBaseUrl { get; set; }
    public bool Install { get; set; }

    [Output]
    public string CliPath { get; set; } = "";

    public void Cancel() => cancellation.Cancel();

    public void Dispose() => cancellation.Dispose();

    public override bool Execute()
    {
        try
        {
            var distribution = SmithyCliDistribution.ForHost();
            var cache = string.IsNullOrWhiteSpace(CachePath)
                ? SmithyCliInstallation.DefaultCachePath
                : CachePath;
            var installation = new SmithyCliInstallation(Path.GetFullPath(cache), distribution);
            if (Install && !installation.IsInstalled)
            {
                var url = new Uri(
                    string.IsNullOrWhiteSpace(DownloadBaseUrl)
                        ? SmithyCliDistribution.DownloadBaseUrl
                        : DownloadBaseUrl
                );
                if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                    throw new ArgumentException("SmithyCliDownloadBaseUrl must be an HTTP(S) URL.");
                Log.LogMessage(
                    MessageImportance.High,
                    "NSmithy: installing Smithy CLI {0} for {1} into {2}",
                    distribution.Version,
                    distribution.Platform,
                    cache
                );
                using var client = new HttpClient();
                installation.InstallAsync(client, url, cancellation.Token).GetAwaiter().GetResult();
            }
            if (!installation.IsInstalled)
            {
                Log.LogError(
                    "NSmithy: Smithy CLI {0} for {1} is not installed in {2}. Run 'dotnet nsmithy install --project <project.csproj>' or 'dotnet msbuild <project.csproj> -t:InstallSmithyCli' after dotnet restore, or set SmithyCliPath to an installed CLI.",
                    distribution.Version,
                    distribution.Platform,
                    cache
                );
                return false;
            }
            CliPath = installation.LauncherPath;
            Log.LogMessage(MessageImportance.Low, "NSmithy: using {0}", CliPath);
            return true;
        }
        catch (Exception exception)
            when (exception
                    is InvalidDataException
                        or IOException
                        or HttpRequestException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or FormatException
                        or PlatformNotSupportedException
                        or OperationCanceledException
            )
        {
            Log.LogError(
                "NSmithy: could not {0} the Smithy CLI: {1}",
                Install ? "install" : "resolve",
                exception.Message
            );
            return false;
        }
    }
}

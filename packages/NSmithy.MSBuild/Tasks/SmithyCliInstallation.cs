using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NSmithy.Contracts;

// This pin belongs to the project's MSBuild package, not to the globally installed dotnet tool.
internal sealed record SmithyCliDistribution(string Version, string Platform, string Sha256)
{
    internal const string DownloadBaseUrl =
        "https://github.com/smithy-lang/smithy/releases/download";

    internal static SmithyCliDistribution ForHost()
    {
        var architecture = RuntimeInformation.OSArchitecture;
        var platform = (
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux(),
            architecture
        ) switch
        {
            (true, _, _, Architecture.X64 or Architecture.Arm64) => "windows-x64",
            (_, true, _, Architecture.Arm64) => "darwin-aarch64",
            (_, true, _, Architecture.X64) => "darwin-x86_64",
            (_, _, true, Architecture.Arm64) => "linux-aarch64",
            (_, _, true, Architecture.X64) => "linux-x86_64",
            _ => throw new PlatformNotSupportedException(
                "No Smithy CLI distribution is available for this host. Set SmithyCliPath to an installed CLI."
            ),
        };
        var checksum = platform switch
        {
            "darwin-aarch64" => "daf789553a20822138bc90b913233374613e1a4515a61358241d5c5489be0be9",
            "darwin-x86_64" => "eb6f7e72245ecf0e3df992314c80dde080e4215716214874a0c3b94f9813562f",
            "linux-aarch64" => "f69295411846274b9e8128f31ffa1d7ad02fa078047e2c4e46d5d85bcba4fc20",
            "linux-x86_64" => "9071a7db052da81ab6f4be1b4d43ea152b44b78217be0dd21d37d9ea5ec1942d",
            "windows-x64" => "32e00abc06f6d1ac9201d8f574bd7a2d62d65eaeb2ea16b3877be18d8febafc2",
            _ => throw new InvalidOperationException("Missing Smithy CLI checksum."),
        };
        return new("1.73.0", platform, checksum);
    }

    internal string Launcher => Platform == "windows-x64" ? "smithy.bat" : "smithy";
    internal string Java => Platform == "windows-x64" ? "java.exe" : "java";
}

internal sealed class SmithyCliInstallation(string cachePath, SmithyCliDistribution distribution)
{
    internal string DirectoryPath =>
        Path.Combine(cachePath, distribution.Version, distribution.Platform);
    internal string LauncherPath => Path.Combine(DirectoryPath, "bin", distribution.Launcher);

    internal static string DefaultCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NSmithy",
            "smithy-cli"
        );

    internal bool IsInstalled =>
        File.Exists(LauncherPath)
        && File.Exists(Path.Combine(DirectoryPath, "bin", distribution.Java))
        && File.Exists(Path.Combine(DirectoryPath, ".complete"))
        && File.ReadAllText(Path.Combine(DirectoryPath, ".complete")) == distribution.Sha256;

    internal async Task InstallAsync(
        HttpClient client,
        Uri downloadBaseUrl,
        CancellationToken cancellationToken
    )
    {
        if (IsInstalled)
            return;

        var versionDirectory = Path.GetDirectoryName(DirectoryPath)!;
        Directory.CreateDirectory(versionDirectory);
        // Keep the lock file: deleting it after unlocking can let another process lock a different inode.
        await using var installLock = await AcquireLockAsync(
            DirectoryPath + ".lock",
            cancellationToken
        );
        if (IsInstalled)
            return;

        var staging = Path.Combine(
            versionDirectory,
            $".{distribution.Platform}-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(staging);
        try
        {
            var archive = Path.Combine(staging, "download.zip");
            var url = new Uri(
                $"{downloadBaseUrl.AbsoluteUri.TrimEnd('/')}/{distribution.Version}/smithy-cli-{distribution.Platform}.zip"
            );
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(archive))
                await response.Content.CopyToAsync(output, cancellationToken);

            await using (var input = File.OpenRead(archive))
            {
                var checksum = Convert.ToHexString(
                    await SHA256.HashDataAsync(input, cancellationToken)
                );
                if (!checksum.Equals(distribution.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Smithy CLI archive checksum does not match the version pinned by NSmithy. The installation was not changed."
                    );
            }

            ZipFile.ExtractToDirectory(archive, staging);
            var extracted = Path.Combine(staging, $"smithy-cli-{distribution.Platform}");
            if (
                !File.Exists(Path.Combine(extracted, "bin", distribution.Launcher))
                || !File.Exists(Path.Combine(extracted, "bin", distribution.Java))
            )
                throw new InvalidDataException(
                    "Smithy CLI archive is missing its launcher or Java runtime."
                );

            if (!OperatingSystem.IsWindows())
            {
                foreach (var executable in Directory.EnumerateFiles(Path.Combine(extracted, "bin")))
                    File.SetUnixFileMode(
                        executable,
                        File.GetUnixFileMode(executable) | UnixFileMode.UserExecute
                    );
                // The macOS distribution also has a runtime helper outside bin/.
                foreach (
                    var helper in Directory.EnumerateFiles(
                        extracted,
                        "jspawnhelper",
                        SearchOption.AllDirectories
                    )
                )
                    File.SetUnixFileMode(
                        helper,
                        File.GetUnixFileMode(helper) | UnixFileMode.UserExecute
                    );
            }
            await File.WriteAllTextAsync(
                Path.Combine(extracted, ".complete"),
                distribution.Sha256,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            // Only incomplete installations are replaced, and only while holding the install lock.
            if (Directory.Exists(DirectoryPath))
                Directory.Delete(DirectoryPath, recursive: true);
            Directory.Move(extracted, DirectoryPath);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }
    }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using NSmithy.Contracts;

namespace NSmithy.Cli.Tests;

public sealed class SmithyCliInstallationTests : IDisposable
{
    private readonly string cache = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-install-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task InstallsVerifiedArchiveAndReusesItWithoutNetwork()
    {
        var (archive, distribution) = CreateArchive();
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        var installation = new SmithyCliInstallation(cache, distribution);

        await installation.InstallAsync(
            client,
            new Uri("https://mirror.example/releases"),
            CancellationToken.None
        );
        await installation.InstallAsync(
            client,
            new Uri("https://unreachable.example"),
            CancellationToken.None
        );

        Assert.True(installation.IsInstalled);
        Assert.Equal(1, handler.Downloads);
        Assert.Equal(
            $"https://mirror.example/releases/{distribution.Version}/smithy-cli-{distribution.Platform}.zip",
            handler.RequestUri!.AbsoluteUri
        );
        if (!OperatingSystem.IsWindows())
        {
            Assert.NotEqual(
                0,
                (int)(File.GetUnixFileMode(installation.LauncherPath) & UnixFileMode.UserExecute)
            );
            Assert.NotEqual(
                0,
                (int)(
                    File.GetUnixFileMode(
                        Path.Combine(installation.DirectoryPath, "bin", distribution.Java)
                    ) & UnixFileMode.UserExecute
                )
            );
        }
    }

    [Fact]
    public async Task RejectsWrongChecksumWithoutPublishingOrLeavingStagingFiles()
    {
        var (archive, distribution) = CreateArchive();
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        var installation = new SmithyCliInstallation(
            cache,
            distribution with
            {
                Sha256 = new string('0', 64),
            }
        );

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installation.InstallAsync(
                client,
                new Uri("https://mirror.example"),
                CancellationToken.None
            )
        );

        Assert.False(Directory.Exists(installation.DirectoryPath));
        Assert.Empty(Directory.GetDirectories(Path.Combine(cache, distribution.Version)));
    }

    [Fact]
    public async Task ConcurrentInstallationsPublishOnlyOneCompleteDistribution()
    {
        var (archive, distribution) = CreateArchive();
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        var first = new SmithyCliInstallation(cache, distribution);
        var second = new SmithyCliInstallation(cache, distribution);

        await Task.WhenAll(
            first.InstallAsync(client, new Uri("https://mirror.example"), CancellationToken.None),
            second.InstallAsync(client, new Uri("https://mirror.example"), CancellationToken.None)
        );

        Assert.True(first.IsInstalled);
        Assert.True(second.IsInstalled);
        Assert.Equal(1, handler.Downloads);
    }

    [Fact]
    public async Task RepairsAnIncompleteInstallation()
    {
        var (archive, distribution) = CreateArchive();
        var installation = new SmithyCliInstallation(cache, distribution);
        Directory.CreateDirectory(Path.GetDirectoryName(installation.LauncherPath)!);
        await File.WriteAllTextAsync(installation.LauncherPath, "incomplete");
        Assert.False(installation.IsInstalled);
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);

        await installation.InstallAsync(
            client,
            new Uri("https://mirror.example"),
            CancellationToken.None
        );

        Assert.True(installation.IsInstalled);
        Assert.Equal("launcher", await File.ReadAllTextAsync(installation.LauncherPath));
    }

    [Fact]
    public async Task RejectsArchiveWithoutJavaRuntime()
    {
        var (archive, distribution) = CreateArchive(includeJava: false);
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        var installation = new SmithyCliInstallation(cache, distribution);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            installation.InstallAsync(
                client,
                new Uri("https://mirror.example"),
                CancellationToken.None
            )
        );

        Assert.False(installation.IsInstalled);
    }

    [Fact]
    public async Task CancellationWhileWaitingForAnotherInstallerDoesNotPublish()
    {
        var (archive, distribution) = CreateArchive();
        var installation = new SmithyCliInstallation(cache, distribution);
        Directory.CreateDirectory(Path.GetDirectoryName(installation.DirectoryPath)!);
        using var installLock = new FileStream(
            installation.DirectoryPath + ".lock",
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        using var handler = new ArchiveHandler(archive);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var install = installation.InstallAsync(
            client,
            new Uri("https://mirror.example"),
            cancellation.Token
        );
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
        Assert.Equal(0, handler.Downloads);
        Assert.False(installation.IsInstalled);
    }

    private static (byte[] Archive, SmithyCliDistribution Distribution) CreateArchive(
        bool includeJava = true
    )
    {
        var distribution = SmithyCliDistribution.ForHost();
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = new Dictionary<string, string> { [distribution.Launcher] = "launcher" };
            if (includeJava)
                files[distribution.Java] = "runtime";
            foreach (var (name, contents) in files)
            {
                var entry = zip.CreateEntry($"smithy-cli-{distribution.Platform}/bin/{name}");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(contents);
            }
        }
        var bytes = stream.ToArray();
        return (bytes, distribution with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        internal int Downloads { get; private set; }
        internal Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Downloads++;
            RequestUri = request.RequestUri;
            // Yield while the install lock is held, exercising concurrent callers.
            await Task.Delay(30, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive),
            };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(cache))
            Directory.Delete(cache, recursive: true);
    }
}

using System.Diagnostics;
using System.Security;
using NSmithy.Contracts;

namespace NSmithy.Cli.Tests;

public sealed class SmithyCliInstallTargetsTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("cached")]
    [InlineData("override")]
    [InlineData("no-model")]
    [InlineData("multi-target")]
    public async Task PackagedTargetsResolveWithoutBuildingOrDownloading(string scenario)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"nsmithy install targets {Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(Path.Combine(directory, "buildTransitive"));
        Directory.CreateDirectory(Path.Combine(directory, "tasks", "net10.0"));
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (
                repository is not null
                && !File.Exists(Path.Combine(repository.FullName, "NSmithy.slnx"))
            )
                repository = repository.Parent;
            Assert.NotNull(repository);
            File.Copy(
                Path.Combine(
                    repository.FullName,
                    "packages",
                    "NSmithy.MSBuild",
                    "Targets",
                    "NSmithy.MSBuild.targets"
                ),
                Path.Combine(directory, "buildTransitive", "NSmithy.MSBuild.targets")
            );
            File.Copy(
                Path.Combine(
                    repository.FullName,
                    "packages",
                    "NSmithy.MSBuild",
                    "Targets",
                    "NSmithy.MSBuild.MultiTargeting.targets"
                ),
                Path.Combine(directory, "Outer.targets")
            );
            File.Copy(
                typeof(SmithyCliInstallation).Assembly.Location,
                Path.Combine(directory, "tasks", "net10.0", "NSmithy.MSBuild.dll")
            );
            var cache = Path.Combine(directory, "cache");
            if (scenario is "cached" or "multi-target")
            {
                var distribution = SmithyCliDistribution.ForHost();
                var installation = new SmithyCliInstallation(cache, distribution);
                Directory.CreateDirectory(Path.Combine(installation.DirectoryPath, "bin"));
                await File.WriteAllTextAsync(installation.LauncherPath, "cached launcher");
                await File.WriteAllTextAsync(
                    Path.Combine(installation.DirectoryPath, "bin", distribution.Java),
                    "cached runtime"
                );
                await File.WriteAllTextAsync(
                    Path.Combine(installation.DirectoryPath, ".complete"),
                    distribution.Sha256
                );
            }
            var explicitCli = Path.Combine(directory, "user-cli");
            await File.WriteAllTextAsync(explicitCli, "user supplied");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(explicitCli, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var properties = scenario switch
            {
                "override" =>
                    $"<SmithyCliPath>{SecurityElement.Escape(explicitCli)}</SmithyCliPath>",
                "multi-target" => "<TargetFrameworks>net10.0;net9.0</TargetFrameworks>",
                _ => "",
            };
            if (scenario != "no-model")
                await File.WriteAllTextAsync(Path.Combine(directory, "smithy-build.json"), "{}");
            var target = scenario is "cached" or "override" or "multi-target"
                ? "InstallSmithyCli"
                : "_ResolveSmithyCli";
            var project = Path.Combine(directory, "Consumer.proj");
            await File.WriteAllTextAsync(
                project,
                $"""
                <Project>
                  <PropertyGroup>
                    <SmithyCliCachePath>{SecurityElement.Escape(cache)}</SmithyCliCachePath>
                    <SmithyCliDownloadBaseUrl>http://127.0.0.1:1/unreachable</SmithyCliDownloadBaseUrl>
                    {properties}
                  </PropertyGroup>
                  <Import Project="buildTransitive/NSmithy.MSBuild.targets" Condition="'$(TargetFrameworks)' == '' or '$(TargetFramework)' != ''" />
                  <Import Project="Outer.targets" Condition="'$(TargetFrameworks)' != '' and '$(TargetFramework)' == ''" />
                  <Target Name="Verify" DependsOnTargets="{target}">
                    <Message Importance="high" Text="Resolved CLI: $(SmithyCliPath)" />
                  </Target>
                </Project>
                """
            );
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    ArgumentList = { "msbuild", project, "-t:Verify", "-nologo", "-nr:false" },
                },
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            var output = await stdout + await stderr;
            if (scenario == "missing")
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.Contains("-t:InstallSmithyCli", output);
                Assert.False(Directory.Exists(cache));
            }
            else
            {
                Assert.True(process.ExitCode == 0, output);
                if (scenario is "cached" or "multi-target")
                    Assert.Contains(
                        Path.Combine(cache, SmithyCliDistribution.ForHost().Version),
                        output
                    );
                if (scenario is "no-model" or "override")
                    Assert.False(Directory.Exists(cache));
                if (scenario == "override")
                {
                    Assert.Contains(explicitCli, output);
                    Assert.Equal("user supplied", await File.ReadAllTextAsync(explicitCli));
                    if (!OperatingSystem.IsWindows())
                        Assert.Equal(
                            UnixFileMode.UserRead | UnixFileMode.UserExecute,
                            File.GetUnixFileMode(explicitCli)
                        );
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

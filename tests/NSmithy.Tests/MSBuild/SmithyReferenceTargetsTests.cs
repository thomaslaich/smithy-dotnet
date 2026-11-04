namespace NSmithy.Tests.MSBuild;

/// <summary>
/// Runs the NSmithy.MSBuild targets on small projects that import them directly, the way NuGet
/// imports the buildTransitive targets: what a project collects from its references, and what a
/// contracts project packs.
/// </summary>
public sealed class SmithyReferenceTargetsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-references-{Guid.NewGuid():N}"
    );

    private readonly string _targets = Path.Combine(
        DotnetProcess.FindRepoRoot(),
        "packages",
        "NSmithy.MSBuild",
        "Targets",
        "NSmithy.MSBuild.targets"
    );

    public SmithyReferenceTargetsTests()
    {
        Directory.CreateDirectory(_directory);
        // A contracts project whose smithy-build.json names model/ and one file of shared/.
        WriteProject("contracts", "<SmithyPublish>true</SmithyPublish>");
        WriteFile(
            "contracts/smithy-build.json",
            """{ "version": "1.0", "sources": ["model"], "imports": ["shared/money.smithy"] }"""
        );
        WriteFile("contracts/model/orders.smithy");
        WriteFile("contracts/model/nested/ast.json");
        WriteFile("contracts/shared/money.smithy");
        WriteFile("contracts/shared/unlisted.smithy");
        // A project that gets the targets without any of the properties a direct reference to
        // NSmithy.MSBuild sets, as through NSmithy.Client.
        WriteProject("plain");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task DirectReferenceContributesTheModelItsBuildFileNames()
    {
        WriteProject(
            "server",
            references: ["contracts", "plain"],
            probe: """
            <Message Importance="high" Text="SOURCE %(SmithySource.Identity)" Condition="'@(SmithySource)' != ''" />
            <Message Importance="high" Text="BUILDFILE %(SmithyContractsBuildFile.Identity)" Condition="'@(SmithyContractsBuildFile)' != ''" />
            """
        );

        var result = await ProbeAsync("server");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("MSB5029", result.Output);
        Assert.Equal(
            [
                "BUILDFILE contracts/smithy-build.json",
                "SOURCE contracts/model/nested/ast.json",
                "SOURCE contracts/model/orders.smithy",
                "SOURCE contracts/shared/money.smithy",
            ],
            Lines(result, "SOURCE", "BUILDFILE")
        );
    }

    [Fact]
    public async Task TransitiveReferenceContributesOnlyIndirectItems()
    {
        WriteProject("server", references: ["contracts"]);
        WriteProject(
            "tests",
            references: ["server"],
            probe: """
            <Message Importance="high" Text="SOURCE %(SmithySource.Identity)" Condition="'@(SmithySource)' != ''" />
            <Message Importance="high" Text="INDIRECT %(_SmithyIndirectReference.SmithyKind) %(_SmithyIndirectReference.Identity)" Condition="'@(_SmithyIndirectReference)' != ''" />
            """
        );

        var result = await ProbeAsync("tests");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [
                "INDIRECT BuildFile contracts/smithy-build.json",
                "INDIRECT Source contracts/model/nested/ast.json",
                "INDIRECT Source contracts/model/orders.smithy",
                "INDIRECT Source contracts/shared/money.smithy",
            ],
            Lines(result, "SOURCE", "INDIRECT")
        );
    }

    [Fact]
    public async Task ContractsPackageCarriesTheModelItsBuildFileNames()
    {
        var result = await DotnetProcess.RunAsync(
            _directory,
            [
                "msbuild",
                Path.Combine(_directory, "contracts", "contracts.proj"),
                "-t:_SmithyPackContracts",
                "-p:PackageId=Acme.Contracts",
                "-getItem:TfmSpecificPackageFile",
                "-nologo",
            ]
        );

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"PackagePath\": \"build/smithy/model/orders.smithy\"", result.Output);
        Assert.Contains("\"PackagePath\": \"build/smithy/model/nested/ast.json\"", result.Output);
        Assert.Contains("\"PackagePath\": \"build/smithy/shared/money.smithy\"", result.Output);
        Assert.Contains("\"PackagePath\": \"buildTransitive/Acme.Contracts.props\"", result.Output);
        Assert.DoesNotContain("unlisted.smithy", result.Output);
    }

    [Fact]
    public async Task PublishingWithoutBuildFileFails()
    {
        WriteProject("nobuildfile", "<SmithyPublish>true</SmithyPublish>");
        WriteProject("server", references: ["nobuildfile"], probe: "");

        var result = await ProbeAsync("server");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error NSMITHYPUBLISH", result.Output);
    }

    private Task<DotnetResult> ProbeAsync(string project) =>
        DotnetProcess.RunAsync(
            _directory,
            [
                "msbuild",
                Path.Combine(_directory, project, $"{project}.proj"),
                "-t:Probe",
                "-nologo",
                "-v:m",
            ]
        );

    private List<string> Lines(DotnetResult result, params string[] prefixes) =>
        result
            .Output.Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                prefixes.Any(prefix => line.StartsWith(prefix + " ", StringComparison.Ordinal))
            )
            .Select(line =>
                line.Replace(_directory + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)
                    .Replace('\\', '/')
            )
            .Order(StringComparer.Ordinal)
            .ToList();

    private void WriteProject(
        string name,
        string properties = "",
        string[]? references = null,
        string? probe = null
    )
    {
        var referenceItems = string.Join(
            "",
            (references ?? []).Select(reference =>
                $"""<ProjectReference Include="../{reference}/{reference}.proj" />"""
            )
        );
        var probeTarget = probe is null
            ? ""
            : $"""
                <Target Name="Probe" DependsOnTargets="_CollectSmithyItemsFromProjectReferences">
                  {probe}
                </Target>
                """;
        WriteFile(
            $"{name}/{name}.proj",
            $"""
            <Project>
              <PropertyGroup>
                <Configuration>{DotnetProcess.Configuration}</Configuration>
                {properties}
              </PropertyGroup>
              <ItemGroup>{referenceItems}</ItemGroup>
              <Import Project="{_targets}" />
              {probeTarget}
            </Project>
            """
        );
    }

    private void WriteFile(string relativePath, string content = "")
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

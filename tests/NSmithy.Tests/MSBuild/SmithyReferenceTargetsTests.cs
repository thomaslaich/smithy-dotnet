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
        // A contracts project whose smithy-build.json names model/ and one file of shared/. It does
        // not set SmithyPublish: a project offers its model to its references either way.
        WriteProject("contracts");
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

    private const string ItemsProbe = """
        <Message Importance="high" Text="SOURCE %(SmithySource.Identity)" Condition="'@(SmithySource)' != ''" />
        <Message Importance="high" Text="BUILDFILE %(SmithyContractsBuildFile.Identity)" Condition="'@(SmithyContractsBuildFile)' != ''" />
        <Message Importance="high" Text="EFFECTIVE $(_SmithyEffectiveBuildFile)" Condition="'$(_SmithyEffectiveBuildFile)' != ''" />
        """;

    private static readonly string[] ContractsItems =
    [
        "BUILDFILE contracts/smithy-build.json",
        "SOURCE contracts/model/nested/ast.json",
        "SOURCE contracts/model/orders.smithy",
        "SOURCE contracts/shared/money.smithy",
    ];

    [Fact]
    public async Task ServiceProjectGetsTheModelItsReferenceNames()
    {
        WriteProject(
            "server",
            "<SmithyService>acme#Shop</SmithyService>",
            references: ["contracts", "plain"],
            probe: ItemsProbe
        );

        var result = await ProbeAsync("server");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("MSB5029", result.Output);
        Assert.Equal(
            [ContractsItems[0], "EFFECTIVE server/obj/smithy-build.json", .. ContractsItems[1..]],
            Lines(result, "SOURCE", "BUILDFILE", "EFFECTIVE")
        );
    }

    [Fact]
    public async Task ServiceProjectGetsTheModelsItsReferencesReach()
    {
        WriteProject("server", references: ["contracts"]);
        WriteProject(
            "client",
            "<SmithyService>acme#Shop</SmithyService>",
            references: ["server"],
            probe: ItemsProbe
        );

        var result = await ProbeAsync("client");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            [ContractsItems[0], "EFFECTIVE client/obj/smithy-build.json", .. ContractsItems[1..]],
            Lines(result, "SOURCE", "BUILDFILE", "EFFECTIVE")
        );
    }

    [Fact]
    public async Task ProjectWithoutModelOrServiceGeneratesNothingFromItsReferences()
    {
        // An implementation or test project: it uses the shapes the contracts project generated.
        WriteProject("server", references: ["contracts"]);
        WriteProject("tests", references: ["contracts", "server"], probe: ItemsProbe);

        var result = await ProbeAsync("tests");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(Lines(result, "SOURCE", "BUILDFILE", "EFFECTIVE"));
        Assert.False(File.Exists(Path.Combine(_directory, "tests", "obj", "smithy-build.json")));
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
                "-p:SmithyPublish=true",
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
    public async Task ProjectThatNoLongerGeneratesRemovesItsShapeManifest()
    {
        // Left over from a build that generated code: the project has since lost its model.
        WriteFile("plain/obj/Smithy/source/csharp-codegen/nsmithy-shapes.json", "{}");

        var result = await DotnetProcess.RunAsync(
            _directory,
            [
                "msbuild",
                Path.Combine(_directory, "plain", "plain.proj"),
                "-t:_PrepareSmithyBuildFile",
                "-nologo",
                "-v:m",
            ]
        );

        Assert.Equal(0, result.ExitCode);
        Assert.False(
            File.Exists(
                Path.Combine(
                    _directory,
                    "plain",
                    "obj",
                    "Smithy",
                    "source",
                    "csharp-codegen",
                    "nsmithy-shapes.json"
                )
            )
        );
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
                <Target Name="Probe" DependsOnTargets="_PrepareSmithyBuildFile">
                  {probe}
                </Target>
                """;
        WriteFile(
            $"{name}/{name}.proj",
            $"""
            <Project>
              <PropertyGroup>
                <Configuration>{DotnetProcess.Configuration}</Configuration>
                <IntermediateOutputPath>obj/</IntermediateOutputPath>
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

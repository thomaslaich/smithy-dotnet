using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SmithyNet.CodeGeneration;
using SmithyNet.CodeGeneration.CSharp;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;
using Xunit.Abstractions;

namespace SmithyNet.Tests.Protocol;

public sealed class OfficialProtocolSuiteTests(
    OfficialProtocolSuiteFixture fixture,
    ITestOutputHelper output
) : IClassFixture<OfficialProtocolSuiteFixture>
{
    private static readonly ShapeId RestJson1Protocol = ShapeId.Parse("aws.protocols#restJson1");
    private static readonly ShapeId SimpleRestJsonProtocol = ShapeId.Parse("alloy#simpleRestJson");

    [Fact]
    public void OfficialAwsRestJson1SuiteIsAvailable()
    {
        var inventory = ProtocolSuiteInventory.Create(fixture.Model, RestJson1Protocol);

        Assert.Equal(158, inventory.RequestCaseCount);
        Assert.Equal(114, inventory.ResponseCaseCount);
        Assert.Equal(191, inventory.MalformedRequestCaseCount);
        Assert.Contains("RestJsonHttpPrefixHeadersArePresent", inventory.RequestCaseIds);
        Assert.Contains("RestJsonHttpPayloadWithStructure", inventory.RequestCaseIds);
        Assert.Contains("RestJsonHttpResponseCode", inventory.ResponseCaseIds);
    }

    [Fact]
    public void OfficialAlloySimpleRestJsonSuiteIsAvailable()
    {
        var inventory = ProtocolSuiteInventory.Create(fixture.Model, SimpleRestJsonProtocol);

        Assert.Equal(23, inventory.RequestCaseCount);
        Assert.Equal(20, inventory.ResponseCaseCount);
        Assert.Equal(0, inventory.MalformedRequestCaseCount);
        Assert.Contains("AddMenuItem", inventory.RequestCaseIds);
        Assert.Contains("RoutingAbcLabel", inventory.RequestCaseIds);
        Assert.Contains("RoundTripDataResponse", inventory.ResponseCaseIds);
    }

    [Fact]
    public void OfficialProtocolCasesAreAllClassifiedForConformance()
    {
        var cases = OfficialProtocolCase
            .Enumerate(fixture.Model, RestJson1Protocol)
            .Concat(OfficialProtocolCase.Enumerate(fixture.Model, SimpleRestJsonProtocol))
            .ToArray();
        var classifications = cases.Select(OfficialProtocolConformanceMatrix.Classify).ToArray();

        Assert.Equal(506, cases.Length);
        Assert.DoesNotContain(
            classifications,
            classification => classification.Status == OfficialProtocolCaseStatus.Unknown
        );
        Assert.All(
            classifications.Where(classification =>
                classification.Status == OfficialProtocolCaseStatus.Skipped
            ),
            classification => Assert.False(string.IsNullOrWhiteSpace(classification.Reason))
        );
    }

    [Fact]
    public void OfficialProtocolConformanceMatrixHasInitialExecutableAllowlist()
    {
        var executableCases = OfficialProtocolCase
            .Enumerate(fixture.Model, RestJson1Protocol)
            .Concat(OfficialProtocolCase.Enumerate(fixture.Model, SimpleRestJsonProtocol))
            .Select(OfficialProtocolConformanceMatrix.Classify)
            .Where(classification => classification.Status == OfficialProtocolCaseStatus.Executable)
            .Select(classification => $"{classification.Case.Kind}:{classification.Case.Id}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(40, executableCases.Count);
        Assert.Contains("Request:AddMenuItem", executableCases);
        Assert.Contains("Response:AddMenuItemResult", executableCases);
        Assert.Contains("Request:CustomCodeInput", executableCases);
        Assert.Contains("Response:CustomCodeOutput", executableCases);
        Assert.Contains("Request:GetEnumInput", executableCases);
        Assert.Contains("Response:GetEnumOutput", executableCases);
        Assert.Contains("Request:GetIntEnumInput", executableCases);
        Assert.Contains("Response:GetIntEnumOutput", executableCases);
        Assert.Contains("Request:GetMenuRequest", executableCases);
        Assert.Contains("Response:GetMenuResponse", executableCases);
        Assert.Contains("Request:HeaderEndpointInput", executableCases);
        Assert.Contains("Request:HealthGet", executableCases);
        Assert.Contains("Request:RoundTripRequest", executableCases);
        Assert.Contains("Response:RoundTripDataResponse", executableCases);
        Assert.Contains("Request:RoutingAbc", executableCases);
        Assert.Contains("Request:RoutingAbcDef", executableCases);
        Assert.Contains("Request:RoutingAbcDefGreedy", executableCases);
        Assert.Contains("Request:RoutingAbcLabel", executableCases);
        Assert.Contains("Request:RoutingAbcXyz", executableCases);
        Assert.Contains("Request:SimpleRestJsonNoneHttpPayloadWithDefault", executableCases);
        Assert.Contains(
            "Request:SimpleRestJsonNoneRequiredHttpPayloadWithDefault",
            executableCases
        );
        Assert.Contains("Request:SimpleRestJsonSomeHttpPayloadWithDefault", executableCases);
        Assert.Contains(
            "Request:SimpleRestJsonSomeRequiredHttpPayloadWithDefault",
            executableCases
        );
        Assert.Contains("Response:NotFoundError", executableCases);
        Assert.Contains("Response:PriceErrorTest", executableCases);
        Assert.Contains("Response:SimpleRestJsonNoneHttpPayloadWithDefault", executableCases);
        Assert.Contains(
            "Response:SimpleRestJsonNoneRequiredHttpPayloadWithDefault",
            executableCases
        );
        Assert.Contains("Response:SimpleRestJsonSomeHttpPayloadWithDefault", executableCases);
        Assert.Contains(
            "Response:SimpleRestJsonSomeRequiredHttpPayloadWithDefault",
            executableCases
        );
        Assert.Contains("Response:headerEndpointResponse", executableCases);
        Assert.Contains("Response:VersionOutput", executableCases);
        Assert.Contains("Request:RestJsonEmptyInputAndEmptyOutput", executableCases);
        Assert.Contains("Request:RestJsonConstantQueryString", executableCases);
        Assert.Contains("Request:HttpQueryParamsOnlyRequest", executableCases);
        Assert.Contains("Request:RestJsonHttpPrefixHeadersArePresent", executableCases);
        Assert.Contains("Request:RestJsonHttpPayloadWithStructure", executableCases);
        Assert.Contains("Response:RestJsonHttpPrefixHeadersArePresent", executableCases);
        Assert.Contains("Response:RestJsonHttpPayloadWithStructure", executableCases);
        Assert.Contains("Response:RestJsonHttpResponseCode", executableCases);
        Assert.Contains("Response:RestJsonHttpResponseCodeWithNoPayload", executableCases);
    }

    [Fact]
    public async Task ExecutableOfficialProtocolCasesPassGeneratedClientConformance()
    {
        var executableCases = OfficialProtocolCase
            .Enumerate(fixture.Model, RestJson1Protocol)
            .Concat(OfficialProtocolCase.Enumerate(fixture.Model, SimpleRestJsonProtocol))
            .Where(testCase =>
                OfficialProtocolConformanceMatrix.Classify(testCase).Status
                == OfficialProtocolCaseStatus.Executable
            )
            .ToArray();

        foreach (var testCase in executableCases)
        {
            await OfficialGeneratedClientConformanceRunner.RunAsync(fixture.Model, testCase);
        }
    }

    [Fact]
    public async Task ExecutableSimpleRestJsonRequestCasesPassGeneratedServerConformance()
    {
        var executableCases = OfficialProtocolCase
            .Enumerate(fixture.Model, SimpleRestJsonProtocol)
            .Where(OfficialGeneratedServerConformanceRunner.CanRunAsync)
            .ToArray();

        foreach (var testCase in executableCases)
        {
            await OfficialGeneratedServerConformanceRunner.RunAsync(fixture.Model, testCase);
        }
    }

    [Fact]
    public async Task OfficialProtocolConformanceMatrixMatchesSnapshot()
    {
        var cases = OfficialProtocolCase
            .Enumerate(fixture.Model, RestJson1Protocol)
            .Concat(OfficialProtocolCase.Enumerate(fixture.Model, SimpleRestJsonProtocol));
        var markdown = OfficialProtocolConformanceMatrixRenderer.Render(cases);
        var snapshotPath = OfficialProtocolConformanceMatrixRenderer.GetRepositoryReportPath();
        var actualPath = OfficialProtocolConformanceMatrixRenderer.GetRepositoryActualReportPath();
        var snapshot = await File.ReadAllTextAsync(snapshotPath);

        output.WriteLine(markdown);

        if (NormalizeLineEndings(snapshot) != NormalizeLineEndings(markdown))
        {
            await File.WriteAllTextAsync(actualPath, markdown);
        }
        else if (File.Exists(actualPath))
        {
            File.Delete(actualPath);
        }

        Assert.True(
            NormalizeLineEndings(snapshot) == NormalizeLineEndings(markdown),
            $"Protocol conformance snapshot is stale. Compare '{snapshotPath}' with '{actualPath}' and update the snapshot if the change is intentional."
        );
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.ReplaceLineEndings("\n");
    }

}

public sealed class OfficialProtocolSuiteFixture : IAsyncLifetime
{
    private TemporaryDirectory? directory;

    public SmithyModel Model { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "smithy-build.json"),
            """
            {
              "version": "1.0",
              "maven": {
                "dependencies": [
                  "software.amazon.smithy:smithy-aws-traits:1.68.0",
                  "software.amazon.smithy:smithy-aws-protocol-tests:1.68.0",
                  "com.disneystreaming.alloy:alloy-core:0.3.38",
                  "com.disneystreaming.alloy:alloy-protocol-tests:0.3.38"
                ]
              }
            }
            """
        );

        var result = await new SmithyBuildRunner().BuildAsync(
            new SmithyBuildOptions(
                directory.Path,
                "smithy-build.json",
                Path.Combine(directory.Path, "smithy-build")
            )
        );
        Model = result.Model;
    }

    public Task DisposeAsync()
    {
        directory?.Dispose();
        return Task.CompletedTask;
    }
}

internal sealed record ProtocolSuiteInventory(
    int RequestCaseCount,
    int ResponseCaseCount,
    int MalformedRequestCaseCount,
    IReadOnlySet<string> RequestCaseIds,
    IReadOnlySet<string> ResponseCaseIds
)
{
    public static ProtocolSuiteInventory Create(SmithyModel model, ShapeId protocol)
    {
        var requestCases = model
            .Shapes.Values.Where(shape => shape.Kind == ShapeKind.Operation)
            .SelectMany(operation =>
                HttpProtocolComplianceCases.ReadRequestTests(model, operation.Id)
            )
            .Where(testCase => testCase.Protocol == protocol)
            .ToArray();
        var responseCases = model
            .Shapes.Values.SelectMany(shape =>
                HttpProtocolComplianceCases.ReadResponseTests(model, shape.Id)
            )
            .Where(testCase => testCase.Protocol == protocol)
            .ToArray();
        var malformedRequestCaseCount = model
            .Shapes.Values.SelectMany(shape =>
                shape.Kind == ShapeKind.Operation
                    ? HttpProtocolComplianceCases
                        .ReadMalformedRequestTests(model, shape.Id)
                        .Where(testCase => testCase.Protocol == protocol)
                    : []
            )
            .Count();

        return new ProtocolSuiteInventory(
            requestCases.Length,
            responseCases.Length,
            malformedRequestCaseCount,
            requestCases.Select(testCase => testCase.Id).ToHashSet(StringComparer.Ordinal),
            responseCases.Select(testCase => testCase.Id).ToHashSet(StringComparer.Ordinal)
        );
    }
}

internal enum OfficialProtocolCaseKind
{
    Request,
    Response,
    MalformedRequest,
}

internal enum OfficialProtocolCaseStatus
{
    Executable,
    Skipped,
    Unknown,
}

internal sealed record OfficialProtocolCase(
    ShapeId Owner,
    ShapeId Protocol,
    OfficialProtocolCaseKind Kind,
    string Id
)
{
    public static IEnumerable<OfficialProtocolCase> Enumerate(SmithyModel model, ShapeId protocol)
    {
        foreach (var shape in model.Shapes.Values)
        {
            if (shape.Kind == ShapeKind.Operation)
            {
                foreach (
                    var requestTest in HttpProtocolComplianceCases
                        .ReadRequestTests(model, shape.Id)
                        .Where(testCase => testCase.Protocol == protocol)
                )
                {
                    yield return new OfficialProtocolCase(
                        shape.Id,
                        protocol,
                        OfficialProtocolCaseKind.Request,
                        requestTest.Id
                    );
                }

                foreach (
                    var malformedTest in HttpProtocolComplianceCases
                        .ReadMalformedRequestTests(model, shape.Id)
                        .Where(testCase => testCase.Protocol == protocol)
                )
                {
                    yield return new OfficialProtocolCase(
                        shape.Id,
                        protocol,
                        OfficialProtocolCaseKind.MalformedRequest,
                        malformedTest.Id
                    );
                }
            }

            foreach (
                var responseTest in HttpProtocolComplianceCases
                    .ReadResponseTests(model, shape.Id)
                    .Where(testCase => testCase.Protocol == protocol)
            )
            {
                yield return new OfficialProtocolCase(
                    shape.Id,
                    protocol,
                    OfficialProtocolCaseKind.Response,
                    responseTest.Id
                );
            }
        }
    }
}

internal sealed record OfficialProtocolCaseClassification(
    OfficialProtocolCase Case,
    OfficialProtocolCaseStatus Status,
    string? Reason
);

internal static class OfficialProtocolConformanceMatrix
{
    private static readonly ShapeId RestJson1Protocol = ShapeId.Parse("aws.protocols#restJson1");
    private static readonly ShapeId SimpleRestJsonProtocol = ShapeId.Parse("alloy#simpleRestJson");

    private static readonly HashSet<(
        ShapeId Protocol,
        OfficialProtocolCaseKind Kind,
        string Id
    )> ExecutableCases =
    [
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "AddMenuItem"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "AddMenuItemResult"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "CustomCodeInput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "CustomCodeOutput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "GetEnumInput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "GetEnumOutput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "GetIntEnumInput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "GetIntEnumOutput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "GetMenuRequest"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "GetMenuResponse"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "HeaderEndpointInput"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "HealthGet"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoundTripRequest"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "RoundTripDataResponse"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbc"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcDef"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcDefGreedy"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcLabel"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcXyz"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "headerEndpointResponse"),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Request,
            "SimpleRestJsonNoneHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Request,
            "SimpleRestJsonNoneRequiredHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Request,
            "SimpleRestJsonSomeHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Request,
            "SimpleRestJsonSomeRequiredHttpPayloadWithDefault"
        ),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "NotFoundError"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "PriceErrorTest"),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Response,
            "SimpleRestJsonNoneHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Response,
            "SimpleRestJsonNoneRequiredHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Response,
            "SimpleRestJsonSomeHttpPayloadWithDefault"
        ),
        (
            SimpleRestJsonProtocol,
            OfficialProtocolCaseKind.Response,
            "SimpleRestJsonSomeRequiredHttpPayloadWithDefault"
        ),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "VersionOutput"),
        (RestJson1Protocol, OfficialProtocolCaseKind.Request, "RestJsonEmptyInputAndEmptyOutput"),
        (RestJson1Protocol, OfficialProtocolCaseKind.Request, "RestJsonConstantQueryString"),
        (RestJson1Protocol, OfficialProtocolCaseKind.Request, "HttpQueryParamsOnlyRequest"),
        (
            RestJson1Protocol,
            OfficialProtocolCaseKind.Request,
            "RestJsonHttpPrefixHeadersArePresent"
        ),
        (RestJson1Protocol, OfficialProtocolCaseKind.Request, "RestJsonHttpPayloadWithStructure"),
        (
            RestJson1Protocol,
            OfficialProtocolCaseKind.Response,
            "RestJsonHttpPrefixHeadersArePresent"
        ),
        (RestJson1Protocol, OfficialProtocolCaseKind.Response, "RestJsonHttpPayloadWithStructure"),
        (RestJson1Protocol, OfficialProtocolCaseKind.Response, "RestJsonHttpResponseCode"),
        (
            RestJson1Protocol,
            OfficialProtocolCaseKind.Response,
            "RestJsonHttpResponseCodeWithNoPayload"
        ),
    ];

    public static OfficialProtocolCaseClassification Classify(OfficialProtocolCase testCase)
    {
        if (ExecutableCases.Contains((testCase.Protocol, testCase.Kind, testCase.Id)))
        {
            return new OfficialProtocolCaseClassification(
                testCase,
                OfficialProtocolCaseStatus.Executable,
                null
            );
        }

        return new OfficialProtocolCaseClassification(
            testCase,
            OfficialProtocolCaseStatus.Skipped,
            GetSkipReason(testCase)
        );
    }

    private static string GetSkipReason(OfficialProtocolCase testCase)
    {
        if (testCase.Kind == OfficialProtocolCaseKind.MalformedRequest)
        {
            return testCase.Protocol == RestJson1Protocol
                ? "restJson1 server generation and malformed request rejection are not implemented."
                : "Malformed request conformance execution is not implemented.";
        }

        if (
            testCase.Protocol == RestJson1Protocol
            && testCase.Owner.Namespace != "aws.protocoltests.restjson"
        )
        {
            return "AWS service-specific restJson1 protocol fixtures are outside the current generated-client slice.";
        }

        if (testCase.Id == "HeaderEndpointInput")
        {
            return "HTTP URI normalization for trailing slashes does not match the official case.";
        }

        if (testCase.Id.Contains("Greedy", StringComparison.Ordinal))
        {
            return "Greedy label URI expansion is not implemented.";
        }

        if (
            testCase.Id.Contains("Endpoint", StringComparison.Ordinal)
            || testCase.Id.Contains("Host", StringComparison.Ordinal)
        )
        {
            return "Endpoint and host-prefix binding traits are not implemented.";
        }

        if (testCase.Id.Contains("Checksum", StringComparison.Ordinal))
        {
            return "HTTP checksum traits are not implemented.";
        }

        if (
            testCase.Id.Contains("OpenUnions", StringComparison.Ordinal)
            || testCase.Id.Contains("Union", StringComparison.Ordinal)
        )
        {
            return "Full union protocol encodings are not implemented.";
        }

        if (testCase.Id.Contains("Preserve", StringComparison.Ordinal))
        {
            return "alloy#preserveKeyOrder behavior is not implemented.";
        }

        if (testCase.Id.Contains("Routing", StringComparison.Ordinal))
        {
            return "Official server routing conformance execution is not implemented.";
        }

        if (testCase.Id.Contains("Validation", StringComparison.Ordinal))
        {
            return "Protocol-aware runtime validation is not implemented.";
        }

        return testCase.Kind switch
        {
            OfficialProtocolCaseKind.Request =>
                "Official request conformance execution is not yet enabled for this case.",
            OfficialProtocolCaseKind.Response =>
                "Official response conformance execution is not yet enabled for this case.",
            _ => "Official conformance execution is not yet enabled for this case.",
        };
    }
}

internal static class OfficialProtocolConformanceMatrixRenderer
{
    private const string GeneratedReportRelativePath = "docs/generated/protocol-conformance.md";

    public static string GetRepositoryReportPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        return Path.Combine(repositoryRoot, GeneratedReportRelativePath);
    }

    public static string GetRepositoryActualReportPath()
    {
        return Path.ChangeExtension(GetRepositoryReportPath(), ".actual.md");
    }

    public static string Render(IEnumerable<OfficialProtocolCase> cases)
    {
        var classifications = cases
            .Select(OfficialProtocolConformanceMatrix.Classify)
            .OrderBy(
                classification => classification.Case.Protocol.ToString(),
                StringComparer.Ordinal
            )
            .ThenBy(classification => classification.Case.Kind)
            .ThenBy(classification => classification.Case.Id, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Official Protocol Conformance Matrix");
        builder.AppendLine();
        builder.AppendLine("| Protocol | Case kind | Executable | Skipped | Total | Conformance |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

        foreach (
            var group in classifications.GroupBy(classification => new
            {
                classification.Case.Protocol,
                classification.Case.Kind,
            })
        )
        {
            var executable = group.Count(classification =>
                classification.Status == OfficialProtocolCaseStatus.Executable
            );
            var skipped = group.Count(classification =>
                classification.Status == OfficialProtocolCaseStatus.Skipped
            );
            var total = executable + skipped;
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"| `{group.Key.Protocol}` | `{group.Key.Kind}` | {executable} | {skipped} | {total} | {CreatePercentage(executable, total)} |"
            );
        }

        builder.AppendLine();
        builder.AppendLine("## Executable Cases");
        builder.AppendLine();
        foreach (
            var classification in classifications.Where(classification =>
                classification.Status == OfficialProtocolCaseStatus.Executable
            )
        )
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- `{classification.Case.Protocol}` `{classification.Case.Kind}` `{classification.Case.Id}`"
            );
        }

        builder.AppendLine();
        builder.AppendLine("## Skipped Cases By Reason");
        builder.AppendLine();
        foreach (
            var group in classifications
                .Where(classification =>
                    classification.Status == OfficialProtocolCaseStatus.Skipped
                )
                .GroupBy(classification => classification.Reason ?? "Unspecified")
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
        )
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"### {group.Key}");
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"- Count: {group.Count()}");
            foreach (
                var classification in group
                    .OrderBy(item => item.Case.Protocol.ToString(), StringComparer.Ordinal)
                    .ThenBy(item => item.Case.Kind)
                    .ThenBy(item => item.Case.Id, StringComparer.Ordinal)
            )
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- `{classification.Case.Protocol}` `{classification.Case.Kind}` `{classification.Case.Id}`"
                );
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string CreatePercentage(int executable, int total)
    {
        return total == 0
            ? "0.0%"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{(double)executable / total * 100:0.0}%"
            );
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "SmithyNet.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not find the Smithy.NET repository root."
            );
    }
}

internal static class OfficialGeneratedClientConformanceRunner
{
    public static async Task RunAsync(SmithyModel model, OfficialProtocolCase testCase)
    {
        if (testCase.Kind is OfficialProtocolCaseKind.MalformedRequest)
        {
            throw new InvalidOperationException(
                $"Malformed request case '{testCase.Id}' cannot run through the generated client conformance runner."
            );
        }

        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);
        var owner = model.GetShape(testCase.Owner);
        var operation = ResolveOperation(model, owner, testCase.Protocol);
        var service = FindContainingService(model, operation.Id, testCase.Protocol);
        var retainedErrors =
            owner.Kind == ShapeKind.Structure && owner.Traits.Has(SmithyPrelude.ErrorTrait)
                ? new[] { owner.Id }
                : [];
        var suppressOutput =
            testCase.Kind == OfficialProtocolCaseKind.Request
            && testCase.Protocol == ShapeId.Parse("alloy#simpleRestJson");
        var suppressInput = testCase.Kind == OfficialProtocolCaseKind.Response;
        var filteredModel = CreateSingleOperationModel(
            model,
            service,
            operation,
            suppressInput,
            suppressOutput,
            retainedErrors
        );
        operation = filteredModel.GetShape(operation.Id);
        service = filteredModel.GetShape(service.Id);

        foreach (var generatedFile in new CSharpShapeGenerator().Generate(filteredModel))
        {
            if (generatedFile.Path.EndsWith("Server.g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.Combine(directory.Path, generatedFile.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generatedFile.Contents);
        }

        await WriteProjectAsync(directory.Path);
        await WriteProgramAsync(directory.Path, filteredModel, service, operation, owner, testCase);

        var build = await RunDotNet(
            directory.Path,
            "build",
            "--no-dependencies"
        );
        Assert.True(
            build.ExitCode == 0,
            $"dotnet build failed for official protocol case '{testCase.Id}' with exit code {build.ExitCode}.{Environment.NewLine}{build.Output}{Environment.NewLine}{build.Error}"
        );

        var run = await RunDotNet(directory.Path, "run", "--no-build", "--no-restore");
        Assert.True(
            run.ExitCode == 0,
            $"dotnet run failed for official protocol case '{testCase.Id}' with exit code {run.ExitCode}.{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}"
        );
    }

    internal static ModelShape FindContainingService(
        SmithyModel model,
        ShapeId operationId,
        ShapeId protocol
    )
    {
        return model
            .Shapes.Values.Where(shape =>
                shape.Kind == ShapeKind.Service
                && shape.Traits.Has(protocol)
                && shape.Operations.Contains(operationId)
            )
            .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
            .First();
    }

    internal static ModelShape ResolveOperation(
        SmithyModel model,
        ModelShape owner,
        ShapeId protocol
    )
    {
        if (owner.Kind == ShapeKind.Operation)
        {
            return owner;
        }

        return model
            .Shapes.Values.Where(shape =>
                shape.Kind == ShapeKind.Operation && shape.Errors.Any(error => error == owner.Id)
            )
            .OrderBy(shape => shape.Id.ToString(), StringComparer.Ordinal)
            .First();
    }

    internal static SmithyModel CreateSingleOperationModel(
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        bool suppressInput,
        bool suppressOutput,
        IReadOnlyList<ShapeId>? retainedErrors = null
    )
    {
        var shapes = new Dictionary<ShapeId, ModelShape>();
        var filteredOperation = operation with
        {
            Errors = retainedErrors is { Count: > 0 } ? [.. retainedErrors] : [],
            Input =
                suppressInput || operation.Input is { } input && IsUnit(input)
                    ? null
                    : operation.Input,
            Output =
                suppressOutput || operation.Output is { } output && IsUnit(output)
                    ? null
                    : operation.Output,
        };
        AddShape(service with { Operations = [operation.Id] });
        AddShape(filteredOperation);
        AddShapeClosure(filteredOperation.Input);
        if (!suppressOutput)
        {
            AddShapeClosure(operation.Output);
        }
        foreach (var error in filteredOperation.Errors)
        {
            AddShapeClosure(error);
        }

        return new SmithyModel(model.SmithyVersion, model.Metadata, shapes);

        void AddShapeClosure(ShapeId? id)
        {
            if (
                id is not { } shapeId
                || shapeId.Namespace == SmithyPrelude.Namespace
                || shapes.ContainsKey(shapeId)
            )
            {
                return;
            }

            var shape = model.GetShape(shapeId);
            AddShape(shape);
            AddShapeClosure(shape.Target);
            AddShapeClosure(shape.Input);
            AddShapeClosure(shape.Output);
            foreach (var error in shape.Errors)
            {
                AddShapeClosure(error);
            }

            foreach (var member in shape.Members.Values)
            {
                AddShapeClosure(member.Target);
            }
        }

        void AddShape(ModelShape shape)
        {
            shapes.TryAdd(shape.Id, shape);
        }

        static bool IsUnit(ShapeId id)
        {
            return id.Namespace == SmithyPrelude.Namespace && id.Name == "Unit";
        }
    }

    private static async Task WriteProjectAsync(string projectDirectory)
    {
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "OfficialProtocolCase.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Client/SmithyNet.Client.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Core/SmithyNet.Core.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Http/SmithyNet.Http.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Json/SmithyNet.Json.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Server.AspNetCore/SmithyNet.Server.AspNetCore.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Server/SmithyNet.Server.csproj" />
              </ItemGroup>
            </Project>
            """
        );
    }

    private static Task WriteProgramAsync(
        string projectDirectory,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        ModelShape owner,
        OfficialProtocolCase testCase
    )
    {
        var serviceNamespace = CSharpIdentifier.Namespace(
            service.Id.Namespace,
            baseNamespace: null
        );
        var serviceTypeName = CSharpIdentifier.TypeName(service.Id.Name);
        var clientTypeName = $"{serviceTypeName}Client";
        var interfaceName = $"I{clientTypeName}";
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        var requestTest =
            testCase.Kind == OfficialProtocolCaseKind.Request
                ? HttpProtocolComplianceCases
                    .ReadRequestTests(model, operation.Id)
                    .Single(item => item.Id == testCase.Id && item.Protocol == testCase.Protocol)
                : null;
        var responseTest =
            testCase.Kind == OfficialProtocolCaseKind.Response
                ? HttpProtocolComplianceCases
                    .ReadResponseTests(model, owner.Id)
                    .Single(item => item.Id == testCase.Id && item.Protocol == testCase.Protocol)
            : requestTest is not null ? CreateSuccessResponseForRequestTest()
            : null;
        var input = CreateClientInput(model, operation, requestTest?.Parameters ?? Document.Null);
        var errorType =
            testCase.Kind == OfficialProtocolCaseKind.Response
            && owner.Kind == ShapeKind.Structure
            && owner.Traits.Has(SmithyPrelude.ErrorTrait)
                ? GetTypeReference(owner.Id, service.Id.Namespace)
                : null;
        var call =
            errorType is not null && responseTest is not null
                ? $$"""
            try
            {
                await client.{{methodName}}({{input}});
                throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                    $"Expected {errorType} for {testCase.Id}"
                )}});
            }
            catch ({{errorType}} error)
            {
                {{ComplianceCSharpLiterals.CreateEqualityAssertion(
                    model,
                    owner.Id,
                    "error",
                    responseTest.Parameters,
                    operation.Id.Namespace,
                    new CSharpGenerationOptions(),
                    $"Unexpected error for {testCase.Id}"
                )}}
            }
            """
            : operation.Output is { } outputId && responseTest is not null
                ? $$"""
            var output = await client.{{methodName}}({{input}});
            {{ComplianceCSharpLiterals.CreateEqualityAssertion(
                model,
                outputId,
                "output",
                responseTest.Parameters,
                operation.Id.Namespace,
                new CSharpGenerationOptions(),
                $"Unexpected output for {testCase.Id}"
            )}}
            """
            : $"await client.{methodName}({input});";
        var handlerCase = requestTest is not null
            ? CreateRequestAssertions(requestTest)
            : string.Empty;
        var requestOnlyWithoutOutput =
            testCase.Kind == OfficialProtocolCaseKind.Request
            && testCase.Protocol == ShapeId.Parse("alloy#simpleRestJson");
        var responseHeaders =
            requestOnlyWithoutOutput ? string.Empty
            : responseTest is not null ? CreateResponseHeaders(responseTest.Headers)
            : string.Empty;
        var responseStatusCode = requestOnlyWithoutOutput ? 204 : responseTest?.Code ?? 200;
        var responseBody =
            requestOnlyWithoutOutput ? string.Empty
            : responseTest is not null ? responseTest.Body ?? string.Empty
            : "{}";

        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            $$"""
            using System.Net;
            using System.Net.Http.Headers;
            using System.Globalization;
            using System.Text;
            using System.Text.Json.Nodes;
            using {{serviceNamespace}};

            {{interfaceName}} client = new {{clientTypeName}}(new HttpClient(new Handler())
            {
                BaseAddress = new Uri("https://example.test")
            });

            {{call}}

            internal sealed class Handler : HttpMessageHandler
            {
                protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    {{handlerCase}}

                    return new HttpResponseMessage((HttpStatusCode){{responseStatusCode.ToString(
                CultureInfo.InvariantCulture
            )}})
                    {
                        Content = new StringContent({{ComplianceCSharpLiterals.FormatString(
                responseBody
            )}}, Encoding.UTF8, "application/json")
                    }{{responseHeaders}};
                }
            }

            internal static class ResponseExtensions
            {
                public static HttpResponseMessage WithHeader(
                    this HttpResponseMessage response,
                    string name,
                    string value)
                {
                    if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                        return response;
                    }

                    response.Headers.Add(name, value);
                    return response;
                }
            }
            """
        );

        HttpResponseTestCase CreateSuccessResponseForRequestTest()
        {
            return new HttpResponseTestCase(
                $"{testCase.Id}SyntheticResponse",
                $"Synthetic response for request conformance case '{testCase.Id}'.",
                testCase.Protocol,
                200,
                requestTest?.Headers
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "{}",
                "application/json",
                Document.From(new Dictionary<string, Document>())
            );
        }
    }

    private static string CreateClientInput(
        SmithyModel model,
        ModelShape operation,
        Document parameters
    )
    {
        if (operation.Input is not { } inputId)
        {
            return string.Empty;
        }

        if (parameters.Kind == DocumentKind.Null && model.GetShape(inputId).Members.Count == 0)
        {
            var inputType = GetTypeReference(inputId, operation.Id.Namespace);
            return $"new {inputType}()";
        }

        return ComplianceCSharpLiterals.CreateValue(
            model,
            inputId,
            parameters.Kind == DocumentKind.Null
                ? Document.From(new Dictionary<string, Document>())
                : parameters,
            operation.Id.Namespace,
            new CSharpGenerationOptions()
        );
    }

    private static string CreateRequestAssertions(HttpRequestTestCase testCase)
    {
        var headerAssertions = string.Join(
            Environment.NewLine,
            testCase.Headers.Select(
                (header, index) =>
                    $$"""
                    if (!TryGetRequestHeader(request, {{ComplianceCSharpLiterals.FormatString(
                        header.Key
                    )}}, out var requestHeader{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}) || !HeaderMatches({{ComplianceCSharpLiterals.FormatString(header.Key)}}, requestHeader{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}.Single(), {{ComplianceCSharpLiterals.FormatString(header.Value)}}))
                    {
                        throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                        $"Unexpected request header: {header.Key}"
                    )}});
                    }
                    """
            )
        );
        var requiredHeaderAssertions = string.Join(
            Environment.NewLine,
            testCase.RequireHeaders.Select(header =>
                $$"""
                    if (!TryGetRequestHeader(request, {{ComplianceCSharpLiterals.FormatString(header)}}, out _))
                    {
                        throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                        $"Missing required request header: {header}"
                    )}});
                    }
                    """
            )
        );
        var forbiddenHeaderAssertions = string.Join(
            Environment.NewLine,
            testCase.ForbidHeaders.Select(header =>
                $$"""
                    if (TryGetRequestHeader(request, {{ComplianceCSharpLiterals.FormatString(header)}}, out _))
                    {
                        throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                        $"Unexpected forbidden request header: {header}"
                    )}});
                    }
                    """
            )
        );
        var requestHeaderHelper =
            testCase.Headers.Count == 0
            && testCase.RequireHeaders.Count == 0
            && testCase.ForbidHeaders.Count == 0
                ? string.Empty
                : """

                            static bool TryGetRequestHeader(
                                HttpRequestMessage request,
                                string name,
                                out IEnumerable<string> values)
                            {
                                if (request.Headers.TryGetValues(name, out values!))
                                {
                                    return true;
                                }

                                if (request.Content?.Headers.TryGetValues(name, out values!) == true)
                                {
                                    return true;
                                }

                                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                                    && request.Content is not null)
                                {
                                    values =
                                    [
                                        request.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture)
                                        ?? request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length.ToString(CultureInfo.InvariantCulture)
                                    ];
                                    return true;
                                }

                                values = [];
                                return false;
                            }

                            static bool HeaderMatches(string name, string actual, string expected)
                            {
                                if (!string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                                {
                                    return actual == expected;
                                }

                                return MediaTypeHeaderValue.TryParse(actual, out var actualMediaType)
                                    && MediaTypeHeaderValue.TryParse(expected, out var expectedMediaType)
                                    && string.Equals(
                                        actualMediaType.MediaType,
                                        expectedMediaType.MediaType,
                                        StringComparison.OrdinalIgnoreCase);
                            }
                    """;
        var expectedAuthority = testCase.ResolvedHost ?? testCase.Host;

        return $$"""
                    if (request.Method.Method != {{ComplianceCSharpLiterals.FormatString(
                testCase.Method
            )}})
                    {
                        throw new InvalidOperationException($"Unexpected request method: {request.Method}");
                    }

                    if (request.RequestUri is null || request.RequestUri.AbsolutePath != {{ComplianceCSharpLiterals.FormatString(
                testCase.Uri.Replace(" ", "%20", StringComparison.Ordinal)
            )}})
                    {
                        throw new InvalidOperationException($"Unexpected request path: {request.RequestUri?.AbsolutePath}");
                    }

                    if (!QueryMatches(request.RequestUri, [{{string.Join(
                ", ",
                testCase.QueryParams.Select(ComplianceCSharpLiterals.FormatString)
            )}}]))
                    {
                        throw new InvalidOperationException($"Unexpected request query: {request.RequestUri?.Query}");
                    }

                    if (!HasRequiredQueryParameters(request.RequestUri, [{{string.Join(
                ", ",
                testCase.RequireQueryParams.Select(ComplianceCSharpLiterals.FormatString)
            )}}]))
                    {
                        throw new InvalidOperationException($"Missing required request query parameter in: {request.RequestUri?.Query}");
                    }

                    if (HasForbiddenQueryParameters(request.RequestUri, [{{string.Join(
                ", ",
                testCase.ForbidQueryParams.Select(ComplianceCSharpLiterals.FormatString)
            )}}]))
                    {
                        throw new InvalidOperationException($"Unexpected forbidden request query parameter in: {request.RequestUri?.Query}");
                    }

                    if (!AuthorityMatches(request.RequestUri, {{ComplianceCSharpLiterals.FormatString(
                expectedAuthority ?? string.Empty
            )}}))
                    {
                        throw new InvalidOperationException($"Unexpected request authority: {request.RequestUri?.Authority}");
                    }

                    {{headerAssertions}}
                    {{requiredHeaderAssertions}}
                    {{forbiddenHeaderAssertions}}

                    var body = request.Content is null
                        ? string.Empty
                        : await request.Content.ReadAsStringAsync(cancellationToken);
                    if (!BodyMatches(body, {{ComplianceCSharpLiterals.FormatString(
                testCase.Body ?? string.Empty
            )}}, {{ComplianceCSharpLiterals.FormatString(testCase.BodyMediaType ?? string.Empty)}}))
                    {
                        throw new InvalidOperationException($"Unexpected request body: {body}");
                    }

                    static bool BodyMatches(string actual, string expected, string bodyMediaType)
                    {
                        if (!string.Equals(bodyMediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                        {
                            return actual == expected;
                        }

                        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
                        {
                            return string.IsNullOrWhiteSpace(actual) && string.IsNullOrWhiteSpace(expected);
                        }

                        return JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected));
                    }

                    static bool QueryMatches(Uri? requestUri, string[] expectedParameters)
                    {
                        return CreateQueryMultiset(ParseQueryEntries(requestUri?.Query))
                            == CreateQueryMultiset(expectedParameters);
                    }

                    static bool HasRequiredQueryParameters(Uri? requestUri, string[] requiredNames)
                    {
                        if (requiredNames.Length == 0)
                        {
                            return true;
                        }

                        var actualNames = GetQueryParameterNames(requestUri);
                        return requiredNames.All(actualNames.Contains);
                    }

                    static bool HasForbiddenQueryParameters(Uri? requestUri, string[] forbiddenNames)
                    {
                        if (forbiddenNames.Length == 0)
                        {
                            return false;
                        }

                        var actualNames = GetQueryParameterNames(requestUri);
                        return forbiddenNames.Any(actualNames.Contains);
                    }

                    static string CreateQueryMultiset(IEnumerable<string> parameters)
                    {
                        return string.Join(
                            "\n",
                            parameters
                                .Where(parameter => !string.IsNullOrEmpty(parameter))
                                .Select(NormalizeQueryParameter)
                                .GroupBy(parameter => parameter, StringComparer.Ordinal)
                                .OrderBy(group => group.Key, StringComparer.Ordinal)
                                .Select(group => $"{group.Key}:{group.Count()}"));
                    }

                    static HashSet<string> GetQueryParameterNames(Uri? requestUri)
                    {
                        return ParseQueryEntries(requestUri?.Query)
                            .Select(GetQueryParameterName)
                            .ToHashSet(StringComparer.Ordinal);
                    }

                    static string[] ParseQueryEntries(string? query)
                    {
                        if (string.IsNullOrEmpty(query))
                        {
                            return [];
                        }

                        var trimmed = query[0] == '?' ? query[1..] : query;
                        return string.IsNullOrEmpty(trimmed)
                            ? []
                            : trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
                    }

                    static string GetQueryParameterName(string parameter)
                    {
                        var separatorIndex = NormalizeQueryParameter(parameter).IndexOf('=');
                        return separatorIndex >= 0
                            ? NormalizeQueryParameter(parameter)[..separatorIndex]
                            : NormalizeQueryParameter(parameter);
                    }

                    static string NormalizeQueryParameter(string parameter)
                    {
                        var separatorIndex = parameter.IndexOf('=');
                        var name = separatorIndex >= 0 ? parameter[..separatorIndex] : parameter;
                        var value = separatorIndex >= 0 ? parameter[(separatorIndex + 1)..] : string.Empty;
                        return separatorIndex >= 0
                            ? $"{DecodeQueryComponent(name)}={DecodeQueryComponent(value)}"
                            : DecodeQueryComponent(name);
                    }

                    static string DecodeQueryComponent(string value)
                    {
                        return Uri.UnescapeDataString(value.Replace("+", "%20", StringComparison.Ordinal));
                    }

                    static bool AuthorityMatches(Uri? requestUri, string expectedAuthority)
                    {
                        if (string.IsNullOrEmpty(expectedAuthority))
                        {
                            return true;
                        }

                        if (requestUri is null)
                        {
                            return false;
                        }

                        var authority = expectedAuthority;
                        var slashIndex = authority.IndexOf('/');
                        if (slashIndex >= 0)
                        {
                            authority = authority[..slashIndex];
                        }

                        return string.Equals(requestUri.Authority, authority, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(requestUri.Host, authority, StringComparison.OrdinalIgnoreCase);
                    }
                    {{requestHeaderHelper}}
            """;
    }

    private static string CreateResponseHeaders(IReadOnlyDictionary<string, string> headers)
    {
        return string.Concat(
            headers.Select(header =>
                $".WithHeader({ComplianceCSharpLiterals.FormatString(header.Key)}, {ComplianceCSharpLiterals.FormatString(header.Value)})"
            )
        );
    }

    internal static string GetTypeReference(ShapeId id, string currentNamespace)
    {
        var typeName = CSharpIdentifier.TypeName(id.Name);
        return string.Equals(id.Namespace, currentNamespace, StringComparison.Ordinal)
            ? typeName
            : $"global::{CSharpIdentifier.Namespace(id.Namespace, baseNamespace: null)}.{typeName}";
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunDotNet(
        string projectDirectory,
        params string[] arguments
    )
    {
        using var process = Process.Start(
            new ProcessStartInfo("dotnet", arguments)
            {
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        );

        Assert.NotNull(process);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await outputTask, await errorTask);
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "SmithyNet.slnx"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not find the Smithy.NET repository root."
            );
    }
}

internal static class OfficialGeneratedServerConformanceRunner
{
    private static readonly HashSet<string> ExecutableRequestCases =
    [
        "HeaderEndpointInput",
        "HealthGet",
        "RoundTripRequest",
        "RoutingAbc",
        "RoutingAbcDef",
        "RoutingAbcLabel",
        "RoutingAbcXyz",
    ];

    public static bool CanRunAsync(OfficialProtocolCase testCase)
    {
        return testCase.Protocol == ShapeId.Parse("alloy#simpleRestJson")
            && testCase.Kind == OfficialProtocolCaseKind.Request
            && OfficialProtocolConformanceMatrix.Classify(testCase).Status
                == OfficialProtocolCaseStatus.Executable
            && ExecutableRequestCases.Contains(testCase.Id);
    }

    public static async Task RunAsync(SmithyModel model, OfficialProtocolCase testCase)
    {
        if (testCase.Kind != OfficialProtocolCaseKind.Request)
        {
            throw new InvalidOperationException(
                $"Generated server conformance only supports request cases. Received '{testCase.Kind}'."
            );
        }

        using var directory = TemporaryDirectory.Create();
        Directory.CreateDirectory(directory.Path);

        var operation = model.GetShape(testCase.Owner);
        var service = OfficialGeneratedClientConformanceRunner.FindContainingService(
            model,
            operation.Id,
            testCase.Protocol
        );
        var filteredModel = OfficialGeneratedClientConformanceRunner.CreateSingleOperationModel(
            model,
            service,
            operation,
            suppressInput: false,
            suppressOutput: true
        );
        operation = filteredModel.GetShape(operation.Id);
        service = filteredModel.GetShape(service.Id);

        foreach (var generatedFile in new CSharpShapeGenerator().Generate(filteredModel))
        {
            if (generatedFile.Path.EndsWith("Client.g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var path = Path.Combine(directory.Path, generatedFile.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generatedFile.Contents);
        }

        await WriteProjectAsync(directory.Path);
        await WriteProgramAsync(directory.Path, filteredModel, service, operation, testCase);

        var build = await OfficialGeneratedClientConformanceRunner.RunDotNet(
            directory.Path,
            "build",
            "--no-dependencies"
        );
        Assert.True(
            build.ExitCode == 0,
            $"dotnet build failed for generated server protocol case '{testCase.Id}' with exit code {build.ExitCode}.{Environment.NewLine}{build.Output}{Environment.NewLine}{build.Error}"
        );

        var run = await OfficialGeneratedClientConformanceRunner.RunDotNet(
            directory.Path,
            "run",
            "--no-build",
            "--no-restore"
        );
        Assert.True(
            run.ExitCode == 0,
            $"dotnet run failed for generated server protocol case '{testCase.Id}' with exit code {run.ExitCode}.{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}"
        );
    }

    private static async Task WriteProjectAsync(string projectDirectory)
    {
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "OfficialProtocolServerCase.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <WarningsNotAsErrors>CS8602;CS8604</WarningsNotAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{OfficialGeneratedClientConformanceRunner.FindRepositoryRoot()}}/src/SmithyNet.Core/SmithyNet.Core.csproj" />
                <ProjectReference Include="{{OfficialGeneratedClientConformanceRunner.FindRepositoryRoot()}}/src/SmithyNet.Json/SmithyNet.Json.csproj" />
                <ProjectReference Include="{{OfficialGeneratedClientConformanceRunner.FindRepositoryRoot()}}/src/SmithyNet.Server.AspNetCore/SmithyNet.Server.AspNetCore.csproj" />
                <ProjectReference Include="{{OfficialGeneratedClientConformanceRunner.FindRepositoryRoot()}}/src/SmithyNet.Server/SmithyNet.Server.csproj" />
              </ItemGroup>
            </Project>
            """
        );
    }

    private static Task WriteProgramAsync(
        string projectDirectory,
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        OfficialProtocolCase testCase
    )
    {
        var requestTest = HttpProtocolComplianceCases
            .ReadRequestTests(model, operation.Id)
            .Single(item => item.Id == testCase.Id && item.Protocol == testCase.Protocol);
        var serviceNamespace = CSharpIdentifier.Namespace(
            service.Id.Namespace,
            baseNamespace: null
        );
        var serviceTypeName = CSharpIdentifier.TypeName(service.Id.Name);
        var serviceContractName = serviceTypeName.EndsWith("Service", StringComparison.Ordinal)
            ? serviceTypeName
            : $"{serviceTypeName}Service";
        var interfaceName = $"I{serviceContractName}Handler";
        var methodName = $"{CSharpIdentifier.PropertyName(operation.Id.Name)}Async";
        var inputAssertion = operation.Input is { } inputId
            ? ComplianceCSharpLiterals.CreateEqualityAssertion(
                model,
                inputId,
                "input",
                requestTest.Parameters.Kind == DocumentKind.Null
                    ? Document.From(new Dictionary<string, Document>())
                    : requestTest.Parameters,
                operation.Id.Namespace,
                new CSharpGenerationOptions(),
                $"Unexpected input for {testCase.Id}"
            )
            : string.Empty;
        var requestPath =
            requestTest.QueryParams.Count == 0
                ? requestTest.Uri
                : $"{requestTest.Uri}?{string.Join("&", requestTest.QueryParams)}";
        var requestContent = CreateServerRequestContent(requestTest);

        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            $$"""
            using System.Net;
            using System.Net.Sockets;
            using System.Text;
            using Microsoft.Extensions.Logging;
            using {{serviceNamespace}};

            var port = GetFreePort();
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Logging.ClearProviders();
            builder.Services.Add{{serviceContractName}}Handler<Handler>();

            await using var app = builder.Build();
            app.UseDeveloperExceptionPage();
            app.Map{{serviceContractName}}();
            await app.StartAsync();

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };
            using var request = new HttpRequestMessage(new HttpMethod({{ComplianceCSharpLiterals.FormatString(
                requestTest.Method
            )}}), {{ComplianceCSharpLiterals.FormatString(requestPath)}});
            {{requestContent}}
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.NoContent)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    "Expected 204 response for "
                    + response.RequestMessage?.RequestUri
                    + ", got "
                    + (int)response.StatusCode
                    + ": "
                    + responseBody);
            }

            if (!Handler.Handled)
            {
                throw new InvalidOperationException("Generated server handler was not invoked.");
            }

            await app.StopAsync();

            static int GetFreePort()
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }

            internal sealed class Handler : {{interfaceName}}
            {
                public static bool Handled { get; private set; }

                public Task {{methodName}}({{CreateHandlerParameters(model, operation)}})
                {
                    Handled = true;
                    {{inputAssertion}}
                    return Task.CompletedTask;
                }
            }
            """
        );
    }

    private static string CreateHandlerParameters(SmithyModel model, ModelShape operation)
    {
        if (operation.Input is not { } inputId)
        {
            return "CancellationToken cancellationToken = default";
        }

        var inputType = OfficialGeneratedClientConformanceRunner.GetTypeReference(
            inputId,
            operation.Id.Namespace
        );
        return $"{inputType} input, CancellationToken cancellationToken = default";
    }

    private static string CreateServerRequestContent(HttpRequestTestCase testCase)
    {
        var lines = new List<string>();
        foreach (var header in testCase.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines.Add(
                $"request.Headers.TryAddWithoutValidation({ComplianceCSharpLiterals.FormatString(header.Key)}, {ComplianceCSharpLiterals.FormatString(header.Value)});"
            );
        }

        if (testCase.Body is not null || testCase.BodyMediaType is not null)
        {
            var mediaType = testCase.BodyMediaType ?? "application/json";
            lines.Add(
                $"request.Content = new StringContent({ComplianceCSharpLiterals.FormatString(testCase.Body ?? string.Empty)}, Encoding.UTF8, {ComplianceCSharpLiterals.FormatString(mediaType)});"
            );
        }

        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private TemporaryDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryDirectory Create()
    {
        return new TemporaryDirectory(
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "smithy-net-official-protocol-tests",
                Guid.NewGuid().ToString("N")
            )
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

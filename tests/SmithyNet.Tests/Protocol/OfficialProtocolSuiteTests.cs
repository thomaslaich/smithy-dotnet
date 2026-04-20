using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
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

        Assert.Equal(9, executableCases.Count);
        Assert.Contains("Request:HealthGet", executableCases);
        Assert.Contains("Request:RoutingAbc", executableCases);
        Assert.Contains("Request:RoutingAbcDef", executableCases);
        Assert.Contains("Request:RoutingAbcLabel", executableCases);
        Assert.Contains("Request:RoutingAbcXyz", executableCases);
        Assert.Contains("Response:headerEndpointResponse", executableCases);
        Assert.Contains("Request:RestJsonEmptyInputAndEmptyOutput", executableCases);
        Assert.Contains("Request:RestJsonHttpPrefixHeadersArePresent", executableCases);
        Assert.Contains("Response:RestJsonHttpPrefixHeadersArePresent", executableCases);
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
    private static readonly ShapeId HttpMalformedRequestTestsTrait = ShapeId.Parse(
        "smithy.test#httpMalformedRequestTests"
    );

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
            .Shapes.Values.SelectMany(operation =>
                HttpProtocolComplianceCases.ReadResponseTests(model, operation.Id)
            )
            .Where(testCase => testCase.Protocol == protocol)
            .ToArray();
        var malformedRequestCaseCount = model
            .Shapes.Values.SelectMany(shape =>
                shape.Traits.GetValueOrDefault(HttpMalformedRequestTestsTrait) is { } malformed
                    ? malformed
                        .AsArray()
                        .Where(testCase =>
                            ShapeId.Parse(testCase.AsObject()["protocol"].AsString()) == protocol
                        )
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
    private static readonly ShapeId HttpMalformedRequestTestsTrait = ShapeId.Parse(
        "smithy.test#httpMalformedRequestTests"
    );

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

            if (shape.Traits.GetValueOrDefault(HttpMalformedRequestTestsTrait) is not { } malformed)
            {
                continue;
            }

            foreach (var malformedTest in malformed.AsArray())
            {
                var properties = malformedTest.AsObject();
                if (ShapeId.Parse(properties["protocol"].AsString()) != protocol)
                {
                    continue;
                }

                yield return new OfficialProtocolCase(
                    shape.Id,
                    protocol,
                    OfficialProtocolCaseKind.MalformedRequest,
                    properties["id"].AsString()
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
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "HealthGet"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbc"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcDef"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcLabel"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Request, "RoutingAbcXyz"),
        (SimpleRestJsonProtocol, OfficialProtocolCaseKind.Response, "headerEndpointResponse"),
        (RestJson1Protocol, OfficialProtocolCaseKind.Request, "RestJsonEmptyInputAndEmptyOutput"),
        (
            RestJson1Protocol,
            OfficialProtocolCaseKind.Request,
            "RestJsonHttpPrefixHeadersArePresent"
        ),
        (
            RestJson1Protocol,
            OfficialProtocolCaseKind.Response,
            "RestJsonHttpPrefixHeadersArePresent"
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
        var operation = model.GetShape(testCase.Owner);
        var service = FindContainingService(model, operation.Id, testCase.Protocol);
        var suppressOutput =
            testCase.Kind == OfficialProtocolCaseKind.Request
            && testCase.Protocol == ShapeId.Parse("alloy#simpleRestJson");
        var filteredModel = CreateSingleOperationModel(model, service, operation, suppressOutput);
        operation = filteredModel.GetShape(operation.Id);
        service = filteredModel.GetShape(service.Id);

        foreach (var generatedFile in new CSharpShapeGenerator().Generate(filteredModel))
        {
            var path = Path.Combine(directory.Path, generatedFile.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generatedFile.Contents);
        }

        await WriteProjectAsync(directory.Path);
        await WriteProgramAsync(directory.Path, filteredModel, service, operation, testCase);

        var build = await RunDotNet(
            directory.Path,
            "build",
            "-p:UseSharedCompilation=false",
            "--disable-build-servers"
        );
        Assert.True(
            build.ExitCode == 0,
            $"dotnet build failed for official protocol case '{testCase.Id}' with exit code {build.ExitCode}.{Environment.NewLine}{build.Output}{Environment.NewLine}{build.Error}"
        );

        var run = await RunDotNet(directory.Path, "run", "--no-build");
        Assert.True(
            run.ExitCode == 0,
            $"dotnet run failed for official protocol case '{testCase.Id}' with exit code {run.ExitCode}.{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}"
        );
    }

    private static ModelShape FindContainingService(
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

    private static SmithyModel CreateSingleOperationModel(
        SmithyModel model,
        ModelShape service,
        ModelShape operation,
        bool suppressOutput
    )
    {
        var shapes = new Dictionary<ShapeId, ModelShape>();
        var filteredOperation = operation with
        {
            Errors = [],
            Input = operation.Input is { } input && IsUnit(input) ? null : operation.Input,
            Output = suppressOutput ? null : operation.Output,
        };
        AddShape(service with { Operations = [operation.Id] });
        AddShape(filteredOperation);
        AddShapeClosure(filteredOperation.Input);
        if (!suppressOutput)
        {
            AddShapeClosure(operation.Output);
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
                    .ReadResponseTests(model, operation.Id)
                    .Single(item => item.Id == testCase.Id && item.Protocol == testCase.Protocol)
            : requestTest is not null ? CreateSuccessResponseForRequestTest()
            : null;
        var input = CreateClientInput(model, operation, requestTest?.Parameters ?? Document.Null);
        var call =
            operation.Output is { } outputId && responseTest is not null
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
        var responseBody = requestOnlyWithoutOutput ? string.Empty : responseTest?.Body ?? "{}";

        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            $$"""
            using System.Net;
            using System.Text;
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
                testCase.Protocol,
                200,
                requestTest?.Headers
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "{}",
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
        var expectedRequestUri =
            testCase.QueryParams.Count == 0
                ? testCase.Uri
                : $"{testCase.Uri}?{string.Join("&", testCase.QueryParams)}";
        var headerAssertions = string.Join(
            Environment.NewLine,
            testCase.Headers.Select(
                (header, index) =>
                    $$"""
                    if (!request.Headers.TryGetValues({{ComplianceCSharpLiterals.FormatString(
                        header.Key
                    )}}, out var requestHeader{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}) || requestHeader{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}.Single() != {{ComplianceCSharpLiterals.FormatString(header.Value)}})
                    {
                        throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                        $"Unexpected request header: {header.Key}"
                    )}});
                    }
                    """
            )
        );

        return $$"""
                    if (request.Method.Method != {{ComplianceCSharpLiterals.FormatString(
                testCase.Method
            )}} || request.RequestUri?.PathAndQuery != {{ComplianceCSharpLiterals.FormatString(
                expectedRequestUri
            )}})
                    {
                        throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri?.PathAndQuery}");
                    }

                    {{headerAssertions}}

                    var body = request.Content is null
                        ? string.Empty
                        : await request.Content.ReadAsStringAsync(cancellationToken);
                    if (body != {{ComplianceCSharpLiterals.FormatString(
                testCase.Body ?? string.Empty
            )}})
                    {
                        throw new InvalidOperationException($"Unexpected request body: {body}");
                    }
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

    private static string GetTypeReference(ShapeId id, string currentNamespace)
    {
        var typeName = CSharpIdentifier.TypeName(id.Name);
        return string.Equals(id.Namespace, currentNamespace, StringComparison.Ordinal)
            ? typeName
            : $"global::{CSharpIdentifier.Namespace(id.Namespace, baseNamespace: null)}.{typeName}";
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunDotNet(
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

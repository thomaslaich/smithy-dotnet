using System.Collections;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class SynthesizeSmithyBuildFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-synthesize-{Guid.NewGuid():N}"
    );

    public SynthesizeSmithyBuildFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void OpenApiPluginWritesIntegerShapesAsIntegers()
    {
        var plugins = Synthesize(openApiProtocol: "aws.protocols#restJson1").GetProperty("plugins");

        var openApi = plugins.GetProperty("openapi");
        Assert.Equal("example#Service", openApi.GetProperty("service").GetString());
        Assert.Equal("aws.protocols#restJson1", openApi.GetProperty("protocol").GetString());
        Assert.True(openApi.GetProperty("useIntegerType").GetBoolean());
    }

    [Fact]
    public void OpenApiPluginIsOmittedWithoutProtocol()
    {
        var plugins = Synthesize(openApiProtocol: "").GetProperty("plugins");

        Assert.False(plugins.TryGetProperty("openapi", out _));
    }

    private JsonElement Synthesize(string openApiProtocol)
    {
        var contractsBuildFile = Path.Combine(_directory, "contracts-smithy-build.json");
        File.WriteAllText(contractsBuildFile, """{ "version": "1.0" }""");
        var outputFile = Path.Combine(_directory, "smithy-build.json");

        var task = new SynthesizeSmithyBuildFile
        {
            BuildEngine = new StubBuildEngine(),
            ContractsBuildFile = contractsBuildFile,
            Sources = [new TaskItem(Path.Combine(_directory, "main.smithy"))],
            Service = "example#Service",
            CSharpCodegenVersion = "0.0.0-SNAPSHOT",
            SmithyVersion = "1.73.0",
            OpenApiProtocol = openApiProtocol,
            OutputFile = outputFile,
        };

        Assert.True(task.Execute());
        using var document = JsonDocument.Parse(File.ReadAllText(outputFile));
        return document.RootElement.Clone();
    }

    private sealed class StubBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs
        ) => false;

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public void LogErrorEvent(BuildErrorEventArgs e) =>
            throw new InvalidOperationException(e.Message);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}

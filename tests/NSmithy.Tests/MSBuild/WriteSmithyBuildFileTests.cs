using System.Collections;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NSmithy.Contracts;

namespace NSmithy.Tests.MSBuild;

public sealed class WriteSmithyBuildFileTests : IDisposable
{
    private const string CodegenArtifact =
        "io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.0.0-SNAPSHOT";

    private const string AwsCodegenArtifact =
        "io.github.thomaslaich.nsmithy:smithy-csharp-aws-codegen:0.0.0-SNAPSHOT";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nsmithy-build-file-{Guid.NewGuid():N}"
    );

    public WriteSmithyBuildFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ContractsReferenceWithoutBuildFileGeneratesFromProperties()
    {
        var contract = WriteFile(
            "contracts/smithy-build.json",
            """
            {
              "version": "1.0",
              "maven": { "dependencies": ["software.amazon.smithy:smithy-aws-traits:1.73.0"] },
              "suppressions": [{ "id": "UnstableTrait", "namespace": "*" }],
              "plugins": { "csharp-codegen": { "service": "ignored#Service" } }
            }
            """
        );
        var source = Path.Combine(_directory, "contracts", "model", "main.smithy");

        var (file, errors) = Write(task =>
        {
            task.ContractsBuildFiles = [new TaskItem(contract)];
            task.Sources = [new TaskItem(source)];
            task.Service = "example#Service";
            task.GenerateServer = false;
        });

        Assert.Empty(errors);
        Assert.Equal(source, file["sources"]![0]!.GetValue<string>());
        Assert.Equal(
            [
                "software.amazon.smithy:smithy-aws-traits:1.73.0",
                CodegenArtifact,
                AwsCodegenArtifact,
            ],
            Dependencies(file)
        );
        Assert.Equal(
            "https://repo.maven.apache.org/maven2/",
            file["maven"]!["repositories"]![0]!["url"]!.GetValue<string>()
        );
        Assert.Equal("UnstableTrait", file["suppressions"]![0]!["id"]!.GetValue<string>());
        var csharp = Plugin(file, "source", "csharp-codegen");
        Assert.Equal("example#Service", csharp["service"]!.GetValue<string>());
        Assert.False(csharp["generateServer"]!.GetValue<bool>());
        Assert.Null(csharp["generateClient"]);
        Assert.Null(file["plugins"]);
    }

    [Fact]
    public void BuildFileSettingsCombineWithProperties()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              // Comments are allowed.
              "version": "1.0",
              "sources": ["model"],
              "plugins": { "csharp-codegen": { "generateFakes": true, "baseNamespace": "Acme" } }
            }
            """
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
        });

        Assert.Empty(errors);
        Assert.Equal(Path.Combine(_directory, "model"), file["sources"]![0]!.GetValue<string>());
        var csharp = Plugin(file, "source", "csharp-codegen");
        Assert.Equal("example#Service", csharp["service"]!.GetValue<string>());
        Assert.True(csharp["generateFakes"]!.GetValue<bool>());
        Assert.Equal("Acme", csharp["baseNamespace"]!.GetValue<string>());
    }

    // Smithy applies top-level plugins to every projection, so other projections keep inheriting
    // the top-level entry that the selected projection's settings are based on.
    [Fact]
    public void TopLevelPluginStaysForOtherProjections()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              "version": "1.0",
              "plugins": { "csharp-codegen": { "baseNamespace": "Acme" } },
              "projections": { "public": { "transforms": [] } }
            }
            """
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
        });

        Assert.Empty(errors);
        var topLevel = file["plugins"]!["csharp-codegen"]!.AsObject();
        Assert.Equal("Acme", topLevel["baseNamespace"]!.GetValue<string>());
        Assert.Null(topLevel["service"]);
        Assert.Null(file["projections"]!["public"]!["plugins"]);
        var source = Plugin(file, "source", "csharp-codegen");
        Assert.Equal("Acme", source["baseNamespace"]!.GetValue<string>());
        Assert.Equal("example#Service", source["service"]!.GetValue<string>());
    }

    // A projection that defines a plugin replaces the top-level one entirely; Smithy does not
    // merge them, so neither does the effective file.
    [Fact]
    public void ProjectionPluginReplacesTopLevelOne()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              "version": "1.0",
              "plugins": { "csharp-codegen": { "generateFakes": true } },
              "projections": {
                "source": { "plugins": { "csharp-codegen": { "baseNamespace": "Acme" } } },
                "other": { "plugins": { "csharp-codegen": { "service": "other#Service" } } }
              }
            }
            """
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
        });

        Assert.Empty(errors);
        var source = Plugin(file, "source", "csharp-codegen");
        Assert.Equal("Acme", source["baseNamespace"]!.GetValue<string>());
        Assert.Equal("example#Service", source["service"]!.GetValue<string>());
        Assert.Null(source["generateFakes"]);
        Assert.True(file["plugins"]!["csharp-codegen"]!["generateFakes"]!.GetValue<bool>());
        var other = Plugin(file, "other", "csharp-codegen");
        Assert.Equal("other#Service", other["service"]!.GetValue<string>());
        Assert.Single(other);
    }

    [Fact]
    public void RepositorySettingsAreKept()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              "version": "1.0",
              "maven": {
                "repositories": [
                  {
                    "id": "internal",
                    "url": "https://maven.example/releases",
                    "httpCredentials": "${MAVEN_USER}:${MAVEN_PASSWORD}",
                    "proxyHost": "http://proxy.example:8080",
                    "proxyCredentials": "${PROXY_USER}:${PROXY_PASSWORD}"
                  }
                ]
              }
            }
            """
        );
        var contract = WriteFile(
            "contracts/smithy-build.json",
            """
            {
              "version": "1.0",
              "maven": {
                "repositories": [
                  { "url": "https://maven.example/releases", "httpCredentials": "other:other" },
                  { "id": "contracts", "url": "https://contracts.example/maven", "httpCredentials": "${TOKEN}:" }
                ]
              }
            }
            """
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.ContractsBuildFiles = [new TaskItem(contract)];
        });

        Assert.Empty(errors);
        var repositories = file["maven"]!["repositories"]!.AsArray();
        Assert.Equal(2, repositories.Count);
        var internalRepository = repositories[0]!.AsObject();
        Assert.Equal("internal", internalRepository["id"]!.GetValue<string>());
        Assert.Equal(
            "${MAVEN_USER}:${MAVEN_PASSWORD}",
            internalRepository["httpCredentials"]!.GetValue<string>()
        );
        Assert.Equal(
            "http://proxy.example:8080",
            internalRepository["proxyHost"]!.GetValue<string>()
        );
        Assert.Equal(
            "${PROXY_USER}:${PROXY_PASSWORD}",
            internalRepository["proxyCredentials"]!.GetValue<string>()
        );
        Assert.Equal("${TOKEN}:", repositories[1]!["httpCredentials"]!.GetValue<string>());
    }

    [Fact]
    public void KeySetByPropertyAndBuildFileFails()
    {
        var project = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "plugins": { "csharp-codegen": { "service": "other#Service" } } }"""
        );

        var (_, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
        });

        var error = Assert.Single(errors);
        Assert.Equal("NSMITHYPLUGIN", error.Code);
        Assert.Contains("SmithyService", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerateClientFalseConflictsWithBuildFileKey()
    {
        var project = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "plugins": { "csharp-codegen": { "generateClient": true } } }"""
        );

        var (_, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
            task.GenerateClient = false;
        });

        Assert.Contains(
            "SmithyGenerateClient",
            Assert.Single(errors).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void BuildFileWithoutServiceKeepsItsPlugins()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              "version": "1.0",
              "maven": { "repositories": [{ "url": "https://mirror.example/maven" }] },
              "projections": {
                "json": { "plugins": { "csharp-codegen": { "service": "tests.json#Fixtures" } } },
                "cbor": { "plugins": { "csharp-codegen": { "service": "tests.cbor#Fixtures" } } }
              }
            }
            """
        );

        var (file, errors) = Write(task => task.ProjectBuildFile = project);

        Assert.Empty(errors);
        Assert.Equal(
            "tests.json#Fixtures",
            Plugin(file, "json", "csharp-codegen")["service"]!.GetValue<string>()
        );
        Assert.Equal(
            "tests.cbor#Fixtures",
            Plugin(file, "cbor", "csharp-codegen")["service"]!.GetValue<string>()
        );
        Assert.Null(file["projections"]!["source"]);
        Assert.Equal([CodegenArtifact, AwsCodegenArtifact], Dependencies(file));
        var repository = Assert.Single(file["maven"]!["repositories"]!.AsArray());
        Assert.Equal("https://mirror.example/maven", repository!["url"]!.GetValue<string>());
    }

    [Fact]
    public void OpenApiDefaultAppliesOnlyWhenBuildFileDoesNotSetIt()
    {
        var project = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "plugins": { "openapi": { "useIntegerType": false, "tags": true } } }"""
        );

        var (withFile, fileErrors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
            task.OpenApiProtocol = "aws.protocols#restJson1";
        });
        var (withoutFile, errors) = Write(task =>
        {
            task.Service = "example#Service";
            task.OpenApiProtocol = "aws.protocols#restJson1";
            task.Sources = [new TaskItem(Path.Combine(_directory, "main.smithy"))];
        });

        Assert.Empty(fileErrors);
        Assert.Empty(errors);
        var configured = Plugin(withFile, "source", "openapi");
        Assert.False(configured["useIntegerType"]!.GetValue<bool>());
        Assert.True(configured["tags"]!.GetValue<bool>());
        Assert.Equal("aws.protocols#restJson1", configured["protocol"]!.GetValue<string>());
        Assert.True(Plugin(withoutFile, "source", "openapi")["useIntegerType"]!.GetValue<bool>());
        Assert.Contains("software.amazon.smithy:smithy-openapi:1.73.0", Dependencies(withoutFile));
    }

    [Fact]
    public void FSharpProjectGivesTheServiceToFSharpCodegen()
    {
        var (file, errors) = Write(task =>
        {
            task.Plugin = "fsharp-codegen";
            task.Service = "example#Service";
            task.GenerateClient = false;
            task.GenerateServer = false;
        });

        Assert.Empty(errors);
        var fsharp = Plugin(file, "source", "fsharp-codegen");
        Assert.Equal("example#Service", fsharp["service"]!.GetValue<string>());
        Assert.False(fsharp["generateClient"]!.GetValue<bool>());
        Assert.Null(fsharp["generateServer"]);
        Assert.Null(file["projections"]!["source"]!["plugins"]!["csharp-codegen"]);
        Assert.Equal(
            ["io.github.thomaslaich.nsmithy:smithy-fsharp-codegen:0.0.0-SNAPSHOT"],
            Dependencies(file)
        );
    }

    [Fact]
    public void ProtoPluginTakesBaseNamespaceFromCSharpCodegen()
    {
        var project = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "plugins": { "csharp-codegen": { "baseNamespace": "Acme" } } }"""
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
            task.Grpc = true;
        });

        Assert.Empty(errors);
        var proto = Plugin(file, "source", "proto-codegen");
        Assert.Equal("Acme", proto["baseNamespace"]!.GetValue<string>());
        Assert.Equal(
            "Grpc",
            proto["fileOptions"]!["csharp_namespace"]!["suffix"]!.GetValue<string>()
        );
        Assert.Contains(
            "io.github.thomaslaich.nsmithy:smithy-proto-codegen:0.0.0-SNAPSHOT",
            Dependencies(file)
        );
    }

    [Fact]
    public void PropertySettingsGoIntoTheSelectedProjection()
    {
        var project = WriteFile(
            "smithy-build.json",
            """{ "version": "1.0", "projections": { "public": { "transforms": [] } } }"""
        );

        var (file, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
            task.Projection = "public";
        });

        Assert.Empty(errors);
        Assert.Equal(
            "example#Service",
            Plugin(file, "public", "csharp-codegen")["service"]!.GetValue<string>()
        );
        Assert.Null(file["projections"]!["source"]);
    }

    [Fact]
    public void MissingProjectionFails()
    {
        var project = WriteFile("smithy-build.json", """{ "version": "1.0" }""");

        var (_, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
            task.Projection = "public";
        });

        Assert.Equal("NSMITHYPROJECTION", Assert.Single(errors).Code);
    }

    [Fact]
    public void DocsWithoutServiceFail()
    {
        var project = WriteFile("smithy-build.json", """{ "version": "1.0" }""");

        var (_, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.GenerateDocs = true;
        });

        var error = Assert.Single(errors);
        Assert.Equal("NSMITHYSERVICE", error.Code);
        Assert.Contains("SmithyGenerateDocs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyAtTwoVersionsFails()
    {
        var project = WriteFile(
            "smithy-build.json",
            """
            {
              "version": "1.0",
              "maven": { "dependencies": ["io.github.thomaslaich.nsmithy:smithy-csharp-codegen:0.9.0"] }
            }
            """
        );

        var (_, errors) = Write(task =>
        {
            task.ProjectBuildFile = project;
            task.Service = "example#Service";
        });

        var error = Assert.Single(errors);
        Assert.Equal("NSMITHYDEPENDENCY", error.Code);
        Assert.Contains("0.9.0", error.Message, StringComparison.Ordinal);
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private (JsonObject File, List<BuildErrorEventArgs> Errors) Write(
        Action<WriteSmithyBuildFile> configure
    )
    {
        var outputFile = Path.Combine(_directory, "obj", $"{Guid.NewGuid():N}.json");
        var engine = new RecordingBuildEngine();
        var task = new WriteSmithyBuildFile
        {
            BuildEngine = engine,
            CSharpCodegenVersion = "0.0.0-SNAPSHOT",
            SmithyVersion = "1.73.0",
            OutputFile = outputFile,
        };
        configure(task);

        var succeeded = task.Execute();

        Assert.Equal(engine.Errors.Count == 0, succeeded);
        var file = succeeded ? JsonNode.Parse(File.ReadAllText(outputFile))!.AsObject() : [];
        return (file, engine.Errors);
    }

    private static JsonObject Plugin(JsonObject file, string projection, string plugin) =>
        file["projections"]![projection]!["plugins"]![plugin]!.AsObject();

    private static List<string> Dependencies(JsonObject file) =>
        [.. file["maven"]!["dependencies"]!.AsArray().Select(node => node!.GetValue<string>())];

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
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

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) { }

        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}

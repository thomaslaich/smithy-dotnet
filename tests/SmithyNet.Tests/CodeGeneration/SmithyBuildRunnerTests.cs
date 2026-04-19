using SmithyNet.CodeGeneration;
using SmithyNet.Core;

namespace SmithyNet.Tests.CodeGeneration;

public sealed class SmithyBuildRunnerTests
{
    [Fact]
    public async Task BuildAsyncRunsSmithyBuildWithProjectionAndReadsModelJson()
    {
        using var directory = TemporaryDirectory.Create();
        var runner = new RecordingSmithyCliRunner(invocation =>
        {
            var outputIndex = invocation.Arguments.ToList().IndexOf("--output");
            var outputDirectory = invocation.Arguments[outputIndex + 1];
            var modelDirectory = Path.Combine(outputDirectory, "sdk", "model");
            Directory.CreateDirectory(modelDirectory);
            File.WriteAllText(Path.Combine(modelDirectory, "model.json"), EmptyModelJson);
            return new SmithyCliRunResult(0, string.Empty, string.Empty);
        });
        var cli = new SmithyBuildRunner(runner);

        var result = await cli.BuildAsync(
            new SmithyBuildOptions(
                directory.Path,
                "smithy-build.json",
                Path.Combine(directory.Path, "smithy-build"),
                "sdk",
                "/tools/smithy"
            )
        );

        Assert.Equal("/tools/smithy", runner.Invocation?.FileName);
        Assert.Equal(directory.Path, runner.Invocation?.WorkingDirectory);
        Assert.Equal(
            [
                "build",
                "--config",
                "smithy-build.json",
                "--output",
                Path.Combine(directory.Path, "smithy-build"),
                "--projection",
                "sdk",
            ],
            runner.Invocation?.Arguments
        );
        Assert.Equal(
            Path.Combine(directory.Path, "smithy-build", "sdk", "model", "model.json"),
            result.ModelPath
        );
        Assert.Equal("2.0", result.Model.SmithyVersion);
    }

    [Fact]
    public async Task BuildAsyncUsesSourceProjectionByDefault()
    {
        using var directory = TemporaryDirectory.Create();
        var outputDirectory = Path.Combine(directory.Path, "smithy-build");
        var runner = new RecordingSmithyCliRunner(_ =>
        {
            var modelDirectory = Path.Combine(outputDirectory, "source", "model");
            Directory.CreateDirectory(modelDirectory);
            File.WriteAllText(Path.Combine(modelDirectory, "model.json"), EmptyModelJson);
            return new SmithyCliRunResult(0, string.Empty, string.Empty);
        });
        var cli = new SmithyBuildRunner(runner);

        var result = await cli.BuildAsync(
            new SmithyBuildOptions(
                directory.Path,
                "smithy-build.json",
                outputDirectory,
                SmithyCliPath: "/tools/smithy"
            )
        );

        Assert.NotNull(runner.Invocation);
        Assert.DoesNotContain("--projection", runner.Invocation.Arguments);
        Assert.Equal(
            Path.Combine(outputDirectory, "source", "model", "model.json"),
            result.ModelPath
        );
    }

    [Fact]
    public async Task BuildAsyncThrowsClearDiagnosticWhenSmithyValidationFails()
    {
        using var directory = TemporaryDirectory.Create();
        var cli = new SmithyBuildRunner(
            new RecordingSmithyCliRunner(_ => new SmithyCliRunResult(
                1,
                string.Empty,
                "ERROR: Shape not found"
            ))
        );

        var exception = await Assert.ThrowsAsync<SmithyException>(() =>
            cli.BuildAsync(
                    new SmithyBuildOptions(
                        directory.Path,
                        "smithy-build.json",
                        Path.Combine(directory.Path, "smithy-build"),
                        SmithyCliPath: "/tools/smithy"
                    )
                )
                .AsTask()
        );

        Assert.Contains("Smithy CLI validation or build failed", exception.Message);
        Assert.Contains("ERROR: Shape not found", exception.Message);
    }

    [Fact]
    public async Task BuildAsyncCallsOutJavaWhenSmithyCliReportsJavaFailure()
    {
        using var directory = TemporaryDirectory.Create();
        var cli = new SmithyBuildRunner(
            new RecordingSmithyCliRunner(_ => new SmithyCliRunResult(
                1,
                string.Empty,
                "java: command not found"
            ))
        );

        var exception = await Assert.ThrowsAsync<SmithyException>(() =>
            cli.BuildAsync(
                    new SmithyBuildOptions(
                        directory.Path,
                        "smithy-build.json",
                        Path.Combine(directory.Path, "smithy-build"),
                        SmithyCliPath: "/tools/smithy"
                    )
                )
                .AsTask()
        );

        Assert.Contains("Verify that Java is installed", exception.Message);
    }

    [Fact]
    public async Task BuildAsyncThrowsWhenModelJsonIsMissing()
    {
        using var directory = TemporaryDirectory.Create();
        var cli = new SmithyBuildRunner(
            new RecordingSmithyCliRunner(_ => new SmithyCliRunResult(0, string.Empty, string.Empty))
        );

        var exception = await Assert.ThrowsAsync<SmithyException>(() =>
            cli.BuildAsync(
                    new SmithyBuildOptions(
                        directory.Path,
                        "smithy-build.json",
                        Path.Combine(directory.Path, "smithy-build"),
                        "sdk",
                        "/tools/smithy"
                    )
                )
                .AsTask()
        );

        Assert.Contains("did not write the expected model JSON AST", exception.Message);
        Assert.Contains(
            Path.Combine("smithy-build", "sdk", "model", "model.json"),
            exception.Message
        );
    }

    [Fact]
    public async Task BuildAsyncRunsRealSmithyCliWithBuildFileSourcesImportsAndProjection()
    {
        using var directory = TemporaryDirectory.Create();
        var modelDirectory = Path.Combine(directory.Path, "model");
        var importsDirectory = Path.Combine(directory.Path, "imports");
        Directory.CreateDirectory(modelDirectory);
        Directory.CreateDirectory(importsDirectory);

        File.WriteAllText(
            Path.Combine(directory.Path, "smithy-build.json"),
            """
            {
              "version": "1.0",
              "sources": ["model"],
              "imports": ["imports/common.smithy"],
              "projections": {
                "sdk": {}
              }
            }
            """
        );
        File.WriteAllText(
            Path.Combine(modelDirectory, "weather.smithy"),
            """
            $version: "2"

            namespace example.weather

            use example.common#BadRequest

            service Weather {
                version: "2026-04-13",
                operations: [GetForecast]
            }

            @readonly
            operation GetForecast {
                input := {
                    @required
                    city: String
                },
                output := {
                    summary: String
                },
                errors: [BadRequest]
            }
            """
        );
        File.WriteAllText(
            Path.Combine(importsDirectory, "common.smithy"),
            """
            $version: "2"

            namespace example.common

            @error("client")
            structure BadRequest {
                message: String
            }
            """
        );

        var result = await new SmithyBuildRunner().BuildAsync(
            new SmithyBuildOptions(
                directory.Path,
                "smithy-build.json",
                Path.Combine(directory.Path, "smithy-build"),
                "sdk"
            )
        );

        var service = result.Model.GetShape(ShapeId.Parse("example.weather#Weather"));
        var operation = result.Model.GetShape(ShapeId.Parse("example.weather#GetForecast"));

        Assert.Equal(
            Path.Combine(directory.Path, "smithy-build", "sdk", "model", "model.json"),
            result.ModelPath
        );
        Assert.Equal("2.0", result.Model.SmithyVersion);
        Assert.Equal(ShapeKind.Service, service.Kind);
        Assert.Equal(ShapeId.Parse("example.weather#GetForecast"), service.Operations.Single());
        Assert.Equal(ShapeId.Parse("example.common#BadRequest"), operation.Errors.Single());
    }

    private const string EmptyModelJson = """
        {
          "smithy": "2.0",
          "shapes": {}
        }
        """;

    private sealed class RecordingSmithyCliRunner(Func<SmithyCliInvocation, SmithyCliRunResult> run)
        : ISmithyCliRunner
    {
        public SmithyCliInvocation? Invocation { get; private set; }

        public ValueTask<SmithyCliRunResult> RunAsync(
            SmithyCliInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Invocation = invocation;
            return ValueTask.FromResult(run(invocation));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
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
                    "smithy-net-tests",
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
}

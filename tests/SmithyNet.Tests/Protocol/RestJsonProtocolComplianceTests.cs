using System.Diagnostics;
using System.Globalization;
using SmithyNet.CodeGeneration;
using SmithyNet.CodeGeneration.CSharp;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.Core;

namespace SmithyNet.Tests.Protocol;

public sealed class RestJsonProtocolComplianceTests
{
    [Fact]
    public async Task GeneratedClientSatisfiesBasicHttpProtocolComplianceCases()
    {
        using var directory = TemporaryDirectory.Create();
        var projectDirectory = directory.Path;
        Directory.CreateDirectory(projectDirectory);

        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#Weather": {
                  "type": "service",
                  "traits": {
                    "aws.protocols#restJson1": {}
                  },
                  "operations": [
                    "example.weather#GetForecast",
                    "example.weather#Ping"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/forecast/{city}"
                    },
                    "smithy.test#httpRequestTests": [
                      {
                        "id": "binds_request_members",
                        "protocol": "aws.protocols#restJson1",
                        "method": "POST",
                        "uri": "/forecast/Zurich",
                        "queryParams": [
                          "units=metric",
                          "debug=true"
                        ],
                        "headers": {
                          "x-request-id": "request-1",
                          "x-meta-trace": "abc"
                        },
                        "body": "{\"note\":\"morning\"}",
                        "bodyMediaType": "application/json",
                        "params": {
                          "city": "Zurich",
                          "details": {
                            "note": "morning"
                          },
                          "metadata": {
                            "trace": "abc"
                          },
                          "requestId": "request-1",
                          "tags": {
                            "debug": "true"
                          },
                          "units": "metric"
                        }
                      }
                    ],
                    "smithy.test#httpResponseTests": [
                      {
                        "id": "binds_response_members",
                        "protocol": "aws.protocols#restJson1",
                        "code": 200,
                        "headers": {
                          "x-request-id": "response-1",
                          "x-extra-source": "server"
                        },
                        "body": "{\"summary\":\"clear\"}",
                        "bodyMediaType": "application/json",
                        "params": {
                          "metadata": {
                            "source": "server"
                          },
                          "requestId": "response-1",
                          "status": 200,
                          "summary": "clear"
                        }
                      }
                    ]
                  },
                  "input": {
                    "target": "example.weather#GetForecastInput"
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
                  }
                },
                "example.weather#ForecastDetails": {
                  "type": "structure",
                  "members": {
                    "note": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.weather#ForecastMetadata": {
                  "type": "map",
                  "members": {
                    "key": {
                      "target": "smithy.api#String"
                    },
                    "value": {
                      "target": "smithy.api#String"
                    }
                  }
                },
                "example.weather#ForecastTags": {
                  "type": "map",
                  "members": {
                    "key": {
                      "target": "smithy.api#String"
                    },
                    "value": {
                      "target": "smithy.api#String"
                    }
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpLabel": {}
                      }
                    },
                    "details": {
                      "target": "example.weather#ForecastDetails",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpPayload": {}
                      }
                    },
                    "metadata": {
                      "target": "example.weather#ForecastMetadata",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpPrefixHeaders": "x-meta-"
                      }
                    },
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-request-id"
                      }
                    },
                    "tags": {
                      "target": "example.weather#ForecastTags",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpQueryParams": {}
                      }
                    },
                    "units": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpQuery": "units"
                      }
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "metadata": {
                      "target": "example.weather#ForecastMetadata",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpPrefixHeaders": "x-extra-"
                      }
                    },
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-request-id"
                      }
                    },
                    "status": {
                      "target": "smithy.api#Integer",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpResponseCode": {}
                      }
                    },
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.weather#Ping": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/ping"
                    },
                    "smithy.test#httpRequestTests": [
                      {
                        "id": "ping_request",
                        "protocol": "aws.protocols#restJson1",
                        "method": "GET",
                        "uri": "/ping",
                        "headers": {
                          "x-ping": "pong"
                        },
                        "params": {
                          "ping": "pong"
                        }
                      }
                    ],
                    "smithy.test#httpResponseTests": [
                      {
                        "id": "ping_response",
                        "protocol": "aws.protocols#restJson1",
                        "code": 200,
                        "headers": {
                          "x-pong": "ok"
                        },
                        "params": {
                          "pong": "ok"
                        }
                      }
                    ]
                  },
                  "input": {
                    "target": "example.weather#PingInput"
                  },
                  "output": {
                    "target": "example.weather#PingOutput"
                  }
                },
                "example.weather#PingInput": {
                  "type": "structure",
                  "members": {
                    "ping": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-ping"
                      }
                    }
                  }
                },
                "example.weather#PingOutput": {
                  "type": "structure",
                  "members": {
                    "pong": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-pong"
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var operationId = ShapeId.Parse("example.weather#GetForecast");
        var requestTest = HttpProtocolComplianceCases
            .ReadRequestTests(model, operationId)
            .Single(test => test.Id == "binds_request_members");
        var responseTest = HttpProtocolComplianceCases
            .ReadResponseTests(model, operationId)
            .Single(test => test.Id == "binds_response_members");
        var pingOperationId = ShapeId.Parse("example.weather#Ping");
        var pingRequestTest = HttpProtocolComplianceCases
            .ReadRequestTests(model, pingOperationId)
            .Single(test => test.Id == "ping_request");
        var pingResponseTest = HttpProtocolComplianceCases
            .ReadResponseTests(model, pingOperationId)
            .Single(test => test.Id == "ping_response");

        Assert.Equal(ShapeId.Parse("aws.protocols#restJson1"), requestTest.Protocol);
        Assert.Equal(ShapeId.Parse("aws.protocols#restJson1"), responseTest.Protocol);
        Assert.Equal(ShapeId.Parse("aws.protocols#restJson1"), pingRequestTest.Protocol);
        Assert.Equal(ShapeId.Parse("aws.protocols#restJson1"), pingResponseTest.Protocol);

        foreach (var generatedFile in new CSharpShapeGenerator().Generate(model))
        {
            var path = Path.Combine(projectDirectory, generatedFile.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generatedFile.Contents);
        }

        await WriteProjectAsync(projectDirectory);
        await WriteProgramAsync(
            projectDirectory,
            model,
            [
                new OperationComplianceCase(
                    operationId,
                    "GetForecastAsync",
                    requestTest,
                    responseTest
                ),
                new OperationComplianceCase(
                    pingOperationId,
                    "PingAsync",
                    pingRequestTest,
                    pingResponseTest
                ),
            ]
        );

        var build = await RunDotNet(
            projectDirectory,
            "build",
            "-p:UseSharedCompilation=false",
            "--disable-build-servers"
        );
        Assert.True(
            build.ExitCode == 0,
            $"dotnet build failed with exit code {build.ExitCode}.{Environment.NewLine}{build.Output}{Environment.NewLine}{build.Error}"
        );

        var run = await RunDotNet(projectDirectory, "run", "--no-build");
        Assert.True(
            run.ExitCode == 0,
            $"dotnet run failed with exit code {run.ExitCode}.{Environment.NewLine}{run.Output}{Environment.NewLine}{run.Error}"
        );
    }

    private static Task WriteProjectAsync(string projectDirectory)
    {
        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "RestJsonComplianceTest.csproj"),
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
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Client/SmithyNet.Client.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Core/SmithyNet.Core.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Http/SmithyNet.Http.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Json/SmithyNet.Json.csproj" />
              </ItemGroup>
            </Project>
            """
        );
    }

    private static Task WriteProgramAsync(
        string projectDirectory,
        SmithyModel model,
        IReadOnlyList<OperationComplianceCase> cases
    )
    {
        var operationCalls = string.Join(
            Environment.NewLine,
            cases.Select(testCase => CreateOperationCall(model, testCase))
        );
        var handlerCases = string.Join(
            Environment.NewLine,
            cases.Select((testCase, index) => CreateHandlerCase(testCase, index == 0))
        );
        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            $$"""
            using System.Net;
            using System.Text;
            using Example.Weather;

            IWeatherClient client = new WeatherClient(new HttpClient(new Handler())
            {
                BaseAddress = new Uri("https://example.test")
            });

            {{operationCalls}}

            internal sealed class Handler : HttpMessageHandler
            {
                protected override async Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    {{handlerCases}}

                    throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri?.PathAndQuery}");
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
    }

    private static string CreateOperationCall(SmithyModel model, OperationComplianceCase testCase)
    {
        var options = new CSharpGenerationOptions();
        var operation = model.GetShape(testCase.OperationId);
        var input = operation.Input is { } inputId
            ? ComplianceCSharpLiterals.CreateValue(
                model,
                inputId,
                testCase.Request.Parameters,
                testCase.OperationId.Namespace,
                options
            )
            : string.Empty;
        var outputAssertions = operation.Output is { } outputId
            ? ComplianceCSharpLiterals.CreateEqualityAssertion(
                model,
                outputId,
                $"{testCase.Request.Id}Output",
                testCase.Response.Parameters,
                testCase.OperationId.Namespace,
                options,
                $"Unexpected response for {testCase.Response.Id}"
            )
            : string.Empty;
        return $$"""
            var {{testCase.Request.Id}}Output = await client.{{testCase.MethodName}}({{input}});
            {{outputAssertions}}
            """;
    }

    private static string CreateHandlerCase(OperationComplianceCase testCase, bool isFirst)
    {
        var expectedRequestUri =
            testCase.Request.QueryParams.Count == 0
                ? testCase.Request.Uri
                : $"{testCase.Request.Uri}?{string.Join("&", testCase.Request.QueryParams)}";
        var requestHeaderAssertions = CreateHeaderAssertions(
            "request.Headers",
            testCase.Request.Headers,
            "request"
        );
        var responseHeaders = CreateResponseHeaders(testCase.Response.Headers);
        var conditionPrefix = isFirst ? "if" : "else if";
        return $$"""
                    {{conditionPrefix}} (request.Method.Method == {{ComplianceCSharpLiterals.FormatString(
                testCase.Request.Method
            )}} && request.RequestUri?.PathAndQuery == {{ComplianceCSharpLiterals.FormatString(
                expectedRequestUri
            )}})
                    {
                        {{requestHeaderAssertions}}

                        var body = request.Content is null
                            ? string.Empty
                            : await request.Content.ReadAsStringAsync(cancellationToken);
                        if (body != {{ComplianceCSharpLiterals.FormatString(
                testCase.Request.Body ?? string.Empty
            )}})
                        {
                            throw new InvalidOperationException($"Unexpected request body: {body}");
                        }

                        return new HttpResponseMessage((HttpStatusCode){{testCase.Response.Code.ToString(
                CultureInfo.InvariantCulture
            )}})
                        {
                            Content = new StringContent(
                                {{ComplianceCSharpLiterals.FormatString(
                testCase.Response.Body ?? string.Empty
            )}},
                                Encoding.UTF8,
                                "application/json")
                        }{{responseHeaders}};
                    }
            """;
    }

    private static string CreateHeaderAssertions(
        string headersExpression,
        IReadOnlyDictionary<string, string> headers,
        string context
    )
    {
        return string.Join(
            Environment.NewLine,
            headers.Select(
                (header, index) =>
                    $$"""
                    if (!{{headersExpression}}.TryGetValues({{ComplianceCSharpLiterals.FormatString(
                        header.Key
                    )}}, out var {{context}}Header{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}) || {{context}}Header{{index.ToString(
                        CultureInfo.InvariantCulture
                    )}}.Single() != {{ComplianceCSharpLiterals.FormatString(header.Value)}})
                    {
                        throw new InvalidOperationException({{ComplianceCSharpLiterals.FormatString(
                        $"Unexpected {context} header: {header.Key}"
                    )}});
                    }
                    """
            )
        );
    }

    private static string CreateResponseHeaders(IReadOnlyDictionary<string, string> headers)
    {
        return string.Concat(
            headers.Select(header =>
                $".WithHeader({ComplianceCSharpLiterals.FormatString(header.Key)}, {ComplianceCSharpLiterals.FormatString(header.Value)})"
            )
        );
    }

    private sealed record OperationComplianceCase(
        ShapeId OperationId,
        string MethodName,
        HttpRequestTestCase Request,
        HttpResponseTestCase Response
    );

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

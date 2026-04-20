using System.Diagnostics;
using SmithyNet.CodeGeneration;
using SmithyNet.CodeGeneration.CSharp;

namespace SmithyNet.Tests.Protocol;

public sealed class SimpleRestJsonServerProtocolTests
{
    [Fact]
    public async Task GeneratedAspNetCoreServerHandlesBasicSimpleRestJsonRequests()
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
                    "alloy#simpleRestJson": {}
                  },
                  "operations": [
                    "example.weather#GetForecast",
                    "example.weather#PutForecast",
                    "example.weather#PutForecastBody",
                    "example.weather#FailForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/forecast/{city}"
                    }
                  },
                  "input": {
                    "target": "example.weather#GetForecastInput"
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
                  }
                },
                "example.weather#PutForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/forecast/{city}"
                    }
                  },
                  "input": {
                    "target": "example.weather#PutForecastInput"
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
                  }
                },
                "example.weather#FailForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/forecast/fail"
                    }
                  },
                  "errors": [
                    "example.weather#BadRequest"
                  ]
                },
                "example.weather#PutForecastBody": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/forecast-body"
                    }
                  },
                  "input": {
                    "target": "example.weather#PutForecastBodyInput"
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
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
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-request-id"
                      }
                    },
                    "metadata": {
                      "target": "example.weather#ForecastMetadata",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpPrefixHeaders": "x-meta-"
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
                "example.weather#PutForecastInput": {
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
                    }
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
                "example.weather#PutForecastBodyInput": {
                  "type": "structure",
                  "members": {
                    "note": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "severity": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-response-id"
                      }
                    },
                    "metadata": {
                      "target": "example.weather#ForecastMetadata",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpPrefixHeaders": "x-extra-"
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
                "example.weather#BadRequest": {
                  "type": "structure",
                  "traits": {
                    "smithy.api#error": "client",
                    "smithy.api#httpError": 400
                  },
                  "members": {
                    "message": {
                      "target": "smithy.api#String"
                    },
                    "reason": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        foreach (var generatedFile in new CSharpShapeGenerator().Generate(model))
        {
            var path = Path.Combine(projectDirectory, generatedFile.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, generatedFile.Contents);
        }

        await WriteProjectAsync(projectDirectory);
        await WriteProgramAsync(projectDirectory);

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
            Path.Combine(projectDirectory, "SimpleRestJsonServerProtocolTest.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
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
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Server.AspNetCore/SmithyNet.Server.AspNetCore.csproj" />
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/SmithyNet.Server/SmithyNet.Server.csproj" />
              </ItemGroup>
            </Project>
            """
        );
    }

    private static Task WriteProgramAsync(string projectDirectory)
    {
        return File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Program.cs"),
            """
            using System.Net;
            using System.Net.Sockets;
            using System.Text;
            using System.Text.Json.Nodes;
            using Example.Weather;

            var port = GetFreePort();
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Logging.ClearProviders();
            builder.Services.AddWeatherServiceHandler<Handler>();

            await using var app = builder.Build();
            app.MapWeatherService();
            await app.StartAsync();

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}")
            };

            using var get = new HttpRequestMessage(HttpMethod.Get, "/forecast/Zurich?units=metric&source=radar");
            get.Headers.Add("x-request-id", "request-1");
            get.Headers.Add("x-meta-trace", "trace-1");
            using var getResponse = await client.SendAsync(get);
            await AssertResponseAsync(
                getResponse,
                HttpStatusCode.Accepted,
                "response-1",
                "server",
                "{\"summary\":\"Zurich:metric:request-1:trace-1:radar\"}");

            using var put = new HttpRequestMessage(HttpMethod.Post, "/forecast/Zurich")
            {
                Content = new StringContent("{\"note\":\"storm\"}", Encoding.UTF8, "application/json")
            };
            using var putResponse = await client.SendAsync(put);
            await AssertResponseAsync(
                putResponse,
                HttpStatusCode.Created,
                "response-2",
                "server",
                "{\"summary\":\"Zurich:storm\"}");

            using var putBody = new HttpRequestMessage(HttpMethod.Post, "/forecast-body")
            {
                Content = new StringContent(
                    "{\"note\":\"rain\",\"severity\":\"high\"}",
                    Encoding.UTF8,
                    "application/json")
            };
            using var putBodyResponse = await client.SendAsync(putBody);
            await AssertResponseAsync(
                putBodyResponse,
                HttpStatusCode.OK,
                "response-3",
                "server",
                "{\"summary\":\"rain:high\"}");

            using var errorResponse = await client.GetAsync("/forecast/fail");
            await AssertResponseAsync(
                errorResponse,
                HttpStatusCode.BadRequest,
                null,
                null,
                "{\"message\":\"bad forecast\",\"reason\":\"invalid\"}");

            await app.StopAsync();

            static int GetFreePort()
            {
                using var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }

            static async Task AssertResponseAsync(
                HttpResponseMessage response,
                HttpStatusCode expectedStatusCode,
                string? expectedResponseId,
                string? expectedExtraSource,
                string expectedBody)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != expectedStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Expected status {(int)expectedStatusCode}, got {(int)response.StatusCode}: {body}");
                }

                if (expectedResponseId is not null)
                {
                    if (!response.Headers.TryGetValues("x-response-id", out var responseIds)
                        || responseIds.Single() != expectedResponseId)
                    {
                        throw new InvalidOperationException("Unexpected response header.");
                    }
                }

                if (expectedExtraSource is not null)
                {
                    if (!response.Headers.TryGetValues("x-extra-source", out var sources)
                        || sources.Single() != expectedExtraSource)
                    {
                        throw new InvalidOperationException("Unexpected prefix response header.");
                    }
                }

                if (!JsonNode.DeepEquals(JsonNode.Parse(body), JsonNode.Parse(expectedBody)))
                {
                    throw new InvalidOperationException($"Unexpected body: {body}");
                }
            }

            internal sealed class Handler : IWeatherServiceHandler
            {
                public Task<GetForecastOutput> GetForecastAsync(
                    GetForecastInput input,
                    CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(
                        new GetForecastOutput(
                            new ForecastMetadata(new Dictionary<string, string> { ["source"] = "server" }),
                            "response-1",
                            202,
                            $"{input.City}:{input.Units}:{input.RequestId}:{input.Metadata.Values["trace"]}:{input.Tags.Values["source"]}"));
                }

                public Task<GetForecastOutput> PutForecastAsync(
                    PutForecastInput input,
                    CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(
                        new GetForecastOutput(
                            new ForecastMetadata(new Dictionary<string, string> { ["source"] = "server" }),
                            "response-2",
                            201,
                            $"{input.City}:{input.Details.Note}"));
                }

                public Task<GetForecastOutput> PutForecastBodyAsync(
                    PutForecastBodyInput input,
                    CancellationToken cancellationToken = default)
                {
                    return Task.FromResult(
                        new GetForecastOutput(
                            new ForecastMetadata(new Dictionary<string, string> { ["source"] = "server" }),
                            "response-3",
                            200,
                            $"{input.Note}:{input.Severity}"));
                }

                public Task FailForecastAsync(CancellationToken cancellationToken = default)
                {
                    throw new BadRequest("bad forecast", "invalid");
                }
            }
            """
        );
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

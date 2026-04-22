using System.Diagnostics;
using SmithyNet.CodeGeneration;
using SmithyNet.CodeGeneration.CSharp;
using SmithyNet.Tests.Assertions;

namespace SmithyNet.Tests.CodeGeneration;

public sealed class CSharpShapeGeneratorTests
{
    [Fact]
    public void GenerateCanFilterBySmithyNamespace()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#Forecast": {
                  "type": "structure"
                },
                "aws.api#Service": {
                  "type": "structure"
                }
              }
            }
            """
        );

        var generatedFiles = new CSharpShapeGenerator().Generate(
            model,
            new CSharpGenerationOptions(GeneratedNamespaces: ["example.weather"])
        );

        var generatedFile = Assert.Single(generatedFiles);
        Assert.Equal("Example/Weather/Forecast.g.cs", generatedFile.Path);
    }

    [Fact]
    public async Task GenerateEmitsCompilableClientForRestJsonService()
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
                    "example.weather#GetForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/forecast/{city}"
                    }
                  },
                  "input": {
                    "target": "example.weather#GetForecastInput"
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
                  },
                  "errors": [
                    "example.weather#BadRequest"
                  ]
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
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-request-id"
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
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
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

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "GeneratedClientCompileTest.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
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

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Consumer.cs"),
            """
            using System.Net;
            using System.Text;
            using Example.Weather;
            using SmithyNet.Client;

            namespace GeneratedClientCompileTest;

            public static class Consumer
            {
                public static async Task<string> ReadAsync(CancellationToken cancellationToken)
                {
                    IWeatherClient client = new WeatherClient(
                        new HttpClient(new Handler()),
                        new SmithyClientOptions
                        {
                            Endpoint = new Uri("https://example.test/api")
                        });
                    var output = await client.GetForecastAsync(
                        new GetForecastInput(
                            "Zurich",
                            new ForecastDetails("morning"),
                            "request-1",
                            "metric"),
                        cancellationToken);
                    return output.Summary;
                }

                private sealed class Handler : HttpMessageHandler
                {
                    protected override Task<HttpResponseMessage> SendAsync(
                        HttpRequestMessage request,
                        CancellationToken cancellationToken)
                    {
                        if (request.Method != HttpMethod.Post || request.RequestUri?.PathAndQuery != "/api/forecast/Zurich?units=metric")
                        {
                            throw new InvalidOperationException("Unexpected request.");
                        }

                        if (!request.Headers.TryGetValues("x-request-id", out var requestIds) || requestIds.Single() != "request-1")
                        {
                            throw new InvalidOperationException("Unexpected request.");
                        }

                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                "{\"summary\":\"clear\"}",
                                Encoding.UTF8,
                                "application/json")
                        }.WithHeader("x-request-id", "response-1"));
                    }
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

        var result = await RunDotNetBuild(projectDirectory);

        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}"
        );
    }

    [Fact]
    public async Task GenerateEmitsCompilableServerForSimpleRestJsonService()
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
                    "example.weather#GetForecast"
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
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpLabel": {}
                      }
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
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

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "GeneratedServerCompileTest.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
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

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Consumer.cs"),
            """
            using Example.Weather;
            using Microsoft.AspNetCore.Routing;
            using SmithyNet.Server;

            namespace GeneratedServerCompileTest;

            public static class Consumer
            {
              public static Task<GetForecastOutput> InvokeDescriptorAsync(CancellationToken cancellationToken)
              {
                return WeatherServiceDescriptor.GetForecast.InvokeAsync(
                  new Handler(),
                  new GetForecastInput("Zurich"),
                  cancellationToken);
              }

                public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints)
                {
                    return endpoints.MapWeatherServiceHttp();
                }

                private sealed class Handler : IWeatherServiceHandler
                {
                    public Task<GetForecastOutput> GetForecastAsync(
                        GetForecastInput input,
                        CancellationToken cancellationToken = default)
                    {
                        return Task.FromResult(new GetForecastOutput(input.City));
                    }
                }
            }
            """
        );

        var result = await RunDotNetBuild(projectDirectory);

        Assert.True(
            result.ExitCode == 0,
            $"dotnet build failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}{Environment.NewLine}{result.Error}"
        );
    }

    [Fact]
    public void GenerateClientBindsRestJsonHttpResponseMembersAndErrors()
    {
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
                    "example.weather#GetForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/forecast"
                    }
                  },
                  "output": {
                    "target": "example.weather#GetForecastOutput"
                  },
                  "errors": [
                    "example.weather#BadRequest"
                  ]
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
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
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpHeader": "x-request-id"
                      }
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

        var client = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherClient.g.cs")
            .Contents;

        Assert.Contains("""GetRequiredHeader<string>(response.Headers, "x-request-id")""", client);
        Assert.Contains("(int)response.StatusCode", client);
        Assert.Contains(
            """DeserializeRequiredBodyMember<string>(response.Content, "summary")""",
            client
        );
        Assert.Contains("if ((int)response.StatusCode == 400)", client);
        Assert.Contains("""GetHeader<string?>(response.Headers, "x-request-id")""", client);
        Assert.Contains("""DeserializeBodyMember<string?>(response.Content, "reason")""", client);
    }

    [Fact]
    public void GenerateClientBindsRestJsonHttpRequestMembers()
    {
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
                    "example.weather#GetForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/forecast/{city}"
                    }
                  },
                  "input": {
                    "target": "example.weather#GetForecastInput"
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
                    "requestId": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpHeader": "x-request-id"
                      }
                    },
                    "units": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpQuery": "units"
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var client = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherClient.g.cs")
            .Contents;

        Assert.Contains(
            """var cityLabel = input.City ?? throw new ArgumentException("HTTP label 'city' is required.", nameof(input));""",
            client
        );
        Assert.Contains(
            """requestUriBuilder.Replace("{city}", Uri.EscapeDataString(FormatHttpValue(cityLabel)));""",
            client
        );
        Assert.Contains("""AppendQuery(requestUriBuilder, "units", input.Units);""", client);
        Assert.Contains("""AddHeader(request.Headers, "x-request-id", input.RequestId);""", client);
        Assert.Contains("request.Content = SmithyJsonSerializer.Serialize(input.Details);", client);
    }

    [Fact]
    public void GenerateClientEmitsTypedClientForSimpleRestJsonService()
    {
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
                    "example.weather#GetForecast"
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
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpLabel": {}
                      }
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        Assert.Contains("Example/Weather/WeatherClient.g.cs", files.Keys);
        Assert.Contains("Example/Weather/WeatherServer.g.cs", files.Keys);
        Assert.Contains(
            "public interface IWeatherClient",
            files["Example/Weather/WeatherClient.g.cs"]
        );
    }

    [Fact]
    public void GenerateClientEmitsGrpcAdapterImplementingSharedClientInterface()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#Weather": {
                  "type": "service",
                  "traits": {
                    "alloy.proto#grpc": {}
                  },
                  "operations": [
                    "example.weather#GetForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "input": {
                    "target": "example.weather#GetForecastInput"
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
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var client = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherClient.g.cs")
            .Contents;

        Assert.Contains("public interface IWeatherClient", client);
        Assert.Contains("public sealed class WeatherGrpcClient : IWeatherClient", client);
        Assert.Contains("public WeatherGrpcClient(ChannelBase channel)", client);
        Assert.Contains(
            "private readonly global::Example.Weather.Grpc.Weather.WeatherClient client;",
            client
        );
        Assert.Contains(
            "var response = await client.GetForecastAsync(request, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);",
            client
        );
        Assert.Contains("return new GetForecastOutput(response.Summary);", client);
    }

    [Fact]
    public void GenerateServerEmitsHandlerInterfaceForSimpleRestJsonService()
    {
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
                    "example.weather#Ping"
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
                "example.weather#Ping": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/ping"
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
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        Assert.Contains("Example/Weather/WeatherServer.g.cs", files.Keys);
        var server = files["Example/Weather/WeatherServer.g.cs"];
        Assert.Contains("public interface IGetForecastHandler", server);
        Assert.Contains("public interface IPingHandler", server);
        Assert.Contains(
            "public interface IWeatherServiceHandler : IGetForecastHandler, IPingHandler",
            server
        );
        Assert.Contains(
            "Task<GetForecastOutput> GetForecastAsync(GetForecastInput input, CancellationToken cancellationToken = default);",
            server
        );
        Assert.Contains("Task PingAsync(CancellationToken cancellationToken = default);", server);
        Assert.Contains("public static class WeatherServiceDescriptor", server);
        Assert.Contains(
            "public static SmithyServiceDescriptor<IWeatherServiceHandler> Service { get; } = new(",
            server
        );
        Assert.Contains(
            "public static SmithyOperationDescriptor<IGetForecastHandler, GetForecastInput, GetForecastOutput> GetForecast { get; } = new(",
            server
        );
        Assert.Contains(
            "static (handler, input, cancellationToken) => handler.GetForecastAsync(input, cancellationToken));",
            server
        );
        Assert.Contains(
            "var output = await WeatherServiceDescriptor.GetForecast.InvokeAsync(handler, input, cancellationToken).ConfigureAwait(false);",
            server
        );
        Assert.Contains(
            "public static IServiceCollection AddWeatherServiceHandler<THandler>(this IServiceCollection services)",
            server
        );
        Assert.Contains(
            "services.AddSingleton<IGetForecastHandler>(serviceProvider => serviceProvider.GetRequiredService<THandler>());",
            server
        );
        Assert.Contains(
            "public static IEndpointRouteBuilder MapWeatherServiceHttp(this IEndpointRouteBuilder endpoints)",
            server
        );
        Assert.Contains(
            """endpoints.MapMethods("/forecast/{city}", ["GET"], async (HttpContext httpContext, IGetForecastHandler handler, CancellationToken cancellationToken) =>""",
            server
        );
    }

    [Fact]
    public void GenerateServerDoesNotDuplicateServiceSuffixForSimpleRestJsonService()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.hello#HelloService": {
                  "type": "service",
                  "traits": {
                    "alloy#simpleRestJson": {}
                  },
                  "operations": [
                    "example.hello#SayHello"
                  ]
                },
                "example.hello#SayHello": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/hello"
                    }
                  },
                  "input": {
                    "target": "example.hello#SayHelloInput"
                  }
                },
                "example.hello#SayHelloInput": {
                  "type": "structure",
                  "members": {
                    "name": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        Assert.Contains("Example/Hello/HelloServiceServer.g.cs", files.Keys);
        var server = files["Example/Hello/HelloServiceServer.g.cs"];
        Assert.Contains("public interface ISayHelloHandler", server);
        Assert.Contains("public interface IHelloServiceHandler : ISayHelloHandler", server);
        Assert.Contains(
            "public static IEndpointRouteBuilder MapHelloServiceHttp(this IEndpointRouteBuilder endpoints)",
            server
        );
        Assert.DoesNotContain("HelloServiceService", server);
    }

    [Fact]
    public void GenerateServerDoesNotEmitHandlerInterfaceForRestJson1Service()
    {
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
                    "example.weather#Ping"
                  ]
                },
                "example.weather#Ping": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "GET",
                      "uri": "/ping"
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator().Generate(model);

        Assert.DoesNotContain(
            files,
            file => file.Path.EndsWith("Server.g.cs", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void GenerateServerEmitsGrpcAdapterForGrpcOnlyService()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.hello#Hello": {
                  "type": "service",
                  "traits": {
                    "alloy.proto#grpc": {}
                  },
                  "operations": [
                    "example.hello#SayHello",
                    "example.hello#Ping"
                  ]
                },
                "example.hello#SayHello": {
                  "type": "operation",
                  "input": {
                    "target": "example.hello#SayHelloInput"
                  },
                  "output": {
                    "target": "example.hello#SayHelloOutput"
                  }
                },
                "example.hello#Ping": {
                  "type": "operation",
                  "output": {
                    "target": "example.hello#PingOutput"
                  }
                },
                "example.hello#SayHelloInput": {
                  "type": "structure",
                  "members": {
                    "name": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.hello#SayHelloOutput": {
                  "type": "structure",
                  "members": {
                    "message": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
                "example.hello#PingOutput": {
                  "type": "structure",
                  "members": {
                    "message": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        Assert.Contains("Example/Hello/HelloServer.g.cs", files.Keys);
        var server = files["Example/Hello/HelloServer.g.cs"];
        Assert.Contains("using Grpc.Core;", server);
        Assert.Contains("using Microsoft.AspNetCore.Builder;", server);
        Assert.Contains(
            "public static IEndpointRouteBuilder MapHelloServiceGrpc(this IEndpointRouteBuilder endpoints)",
            server
        );
        Assert.Contains("endpoints.MapGrpcService<HelloServiceGrpcAdapter>();", server);
        Assert.Contains(
            "public sealed class HelloServiceGrpcAdapter : global::Example.Hello.Grpc.Hello.HelloBase",
            server
        );
        Assert.Contains(
            "public override async Task<global::Example.Hello.Grpc.SayHelloOutput> SayHello(",
            server
        );
        Assert.Contains(
            "var output = await HelloServiceDescriptor.SayHello.InvokeAsync(_handler, new SayHelloInput(request.Name), context.CancellationToken).ConfigureAwait(false);",
            server
        );
        Assert.Contains("new Func<global::Example.Hello.Grpc.SayHelloOutput>(() =>", server);
        Assert.Contains("message.Message = output.Message;", server);
        Assert.Contains(
            "public override async Task<global::Example.Hello.Grpc.PingOutput> Ping(",
            server
        );
        Assert.Contains(
            "await HelloServiceDescriptor.Ping.InvokeAsync(_handler, SmithyUnit.Value, context.CancellationToken).ConfigureAwait(false);",
            server
        );
        Assert.DoesNotContain(
            "public static IEndpointRouteBuilder MapHelloServiceHttp(this IEndpointRouteBuilder endpoints)",
            server
        );
    }

    [Fact]
    public void GenerateServerPreservesGrpcScalarPresenceForOptionalMembers()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#Weather": {
                  "type": "service",
                  "traits": {
                    "alloy.proto#grpc": {}
                  },
                  "operations": [
                    "example.weather#GetForecast"
                  ]
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "input": {
                    "target": "example.weather#GetForecastInput"
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
                        "smithy.api#required": {}
                      }
                    },
                    "days": {
                      "target": "smithy.api#Integer"
                    }
                  }
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        var server = files["Example/Weather/WeatherServer.g.cs"];
        Assert.Contains(
            "new GetForecastInput(request.City, request.HasDays ? request.Days : null)",
            server
        );
        Assert.Contains("if (output.Summary is not null)", server);
        Assert.Contains("message.Summary = output.Summary;", server);
    }

    [Fact]
    public void GenerateEmitsStructureWithRequiredOptionalAndDefaultMembers()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#ForecastRequest": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "days": {
                      "target": "smithy.api#Integer"
                    },
                    "retries": {
                      "target": "smithy.api#Integer",
                      "traits": {
                        "smithy.api#default": 3
                      }
                    },
                    "units": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#default": "metric",
                        "smithy.api#clientOptional": {}
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var file = new CSharpShapeGenerator().Generate(model).Single();

        Assert.Equal("Example/Weather/ForecastRequest.g.cs", file.Path);
        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#ForecastRequest", ShapeKind.Structure)]
            public sealed partial record class ForecastRequest
            {
                public ForecastRequest(string city, int? days = null, int? retries = null, string? units = null)
                {
                    City = city ?? throw new ArgumentNullException(nameof(city));
                    Days = days;
                    Retries = retries ?? 3;
                    Units = units ?? "metric";
                }

                [SmithyMember("city", "smithy.api#String", IsRequired = true)]
                [SmithyTrait("smithy.api#required")]
                public string City { get; }
                [SmithyMember("days", "smithy.api#Integer")]
                public int? Days { get; }
                [SmithyMember("retries", "smithy.api#Integer")]
                [SmithyTrait("smithy.api#default", Value = "3")]
                public int Retries { get; }
                [SmithyMember("units", "smithy.api#String")]
                [SmithyTrait("smithy.api#clientOptional")]
                [SmithyTrait("smithy.api#default", Value = "metric")]
                public string Units { get; }
            }
            """,
            file.Contents
        );
    }

    [Fact]
    public void GenerateUsesAuthoritativeNullabilityForInputMembers()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#ForecastInput": {
                  "type": "structure",
                  "traits": {
                    "smithy.api#input": {}
                  },
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "units": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#default": "metric"
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var file = new CSharpShapeGenerator().Generate(model).Single();

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#ForecastInput", ShapeKind.Structure)]
            [SmithyTrait("smithy.api#input")]
            public sealed partial record class ForecastInput
            {
                public ForecastInput(string city, string? units = null)
                {
                    City = city ?? throw new ArgumentNullException(nameof(city));
                    Units = units ?? "metric";
                }

                [SmithyMember("city", "smithy.api#String", IsRequired = true)]
                [SmithyTrait("smithy.api#required")]
                public string City { get; }
                [SmithyMember("units", "smithy.api#String")]
                [SmithyTrait("smithy.api#default", Value = "metric")]
                public string Units { get; }
            }
            """,
            file.Contents
        );
    }

    [Fact]
    public void GenerateServerUsesRequiredBindingHelpersForRequiredHttpMembers()
    {
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
                    "example.weather#GetForecast"
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "traits": {
                    "smithy.api#input": {}
                  },
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
                    "units": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpQuery": "units"
                      }
                    },
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "locale": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpQuery": "locale"
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var server = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherServer.g.cs")
            .Contents;

        Assert.Contains(
            "SmithyAspNetCoreProtocol.GetRouteValue<string>(httpContext, \"city\")",
            server
        );
        Assert.Contains(
            "SmithyAspNetCoreProtocol.GetRequiredHeaderValue<string>(httpContext, \"x-request-id\")",
            server
        );
        Assert.Contains(
            "SmithyAspNetCoreProtocol.GetRequiredQueryValue<string>(httpContext, \"units\")",
            server
        );
        Assert.Contains(
            "await SmithyAspNetCoreProtocol.ReadRequiredJsonRequestBodyMemberAsync<string>(httpContext, \"summary\", cancellationToken).ConfigureAwait(false)",
            server
        );
        Assert.Contains(
            "SmithyAspNetCoreProtocol.GetQueryValue<string?>(httpContext, \"locale\")",
            server
        );
    }

    [Fact]
    public void GenerateServerBindsInputMembersInConstructorOrder()
    {
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
                    "example.weather#RoundTrip"
                  ]
                },
                "example.weather#RoundTrip": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/round-trip/{label}"
                    }
                  },
                  "input": {
                    "target": "example.weather#RoundTripInput"
                  }
                },
                "example.weather#RoundTripInput": {
                  "type": "structure",
                  "members": {
                    "label": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {},
                        "smithy.api#httpLabel": {}
                      }
                    },
                    "header": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpHeader": "x-header"
                      }
                    },
                    "query": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpQuery": "query"
                      }
                    },
                    "body": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        var server = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherServer.g.cs")
            .Contents;

        var constructorStart = server.IndexOf(
            "var input = new RoundTripInput(",
            StringComparison.Ordinal
        );
        var labelBinding = server.IndexOf(
            "SmithyAspNetCoreProtocol.GetRouteValue<string>(httpContext, \"label\")",
            StringComparison.Ordinal
        );
        var bodyBinding = server.IndexOf(
            "ReadJsonRequestBodyMemberAsync<string",
            StringComparison.Ordinal
        );
        var headerBinding = server.IndexOf("GetHeaderValue<string", StringComparison.Ordinal);
        var queryBinding = server.IndexOf("GetQueryValue<string", StringComparison.Ordinal);

        Assert.True(constructorStart >= 0);
        Assert.True(labelBinding > constructorStart);
        Assert.True(bodyBinding > labelBinding);
        Assert.True(headerBinding > bodyBinding);
        Assert.True(queryBinding > headerBinding);
    }

    [Fact]
    public void GenerateClientBindsResponseMembersInConstructorOrder()
    {
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
                    "example.weather#RoundTrip"
                  ]
                },
                "example.weather#RoundTrip": {
                  "type": "operation",
                  "traits": {
                    "smithy.api#http": {
                      "method": "POST",
                      "uri": "/round-trip"
                    }
                  },
                  "output": {
                    "target": "example.weather#RoundTripOutput"
                  }
                },
                "example.weather#RoundTripOutput": {
                  "type": "structure",
                  "members": {
                    "label": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "header": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#httpHeader": "x-header"
                      }
                    },
                    "query": {
                      "target": "smithy.api#String"
                    },
                    "body": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        var client = new CSharpShapeGenerator()
            .Generate(model)
            .Single(file => file.Path == "Example/Weather/WeatherClient.g.cs")
            .Contents;

        var constructorStart = client.IndexOf(
            "return new RoundTripOutput(",
            StringComparison.Ordinal
        );
        var labelBinding = client.IndexOf(
            "DeserializeRequiredBodyMember<string>(response.Content, \"label\")",
            StringComparison.Ordinal
        );
        var bodyBinding = client.IndexOf(
            "DeserializeBodyMember<string?>(response.Content, \"body\")",
            StringComparison.Ordinal
        );
        var headerBinding = client.IndexOf(
            "GetHeader<string?>(response.Headers, \"x-header\")",
            StringComparison.Ordinal
        );
        var queryBinding = client.IndexOf(
            "DeserializeBodyMember<string?>(response.Content, \"query\")",
            StringComparison.Ordinal
        );

        Assert.True(constructorStart >= 0);
        Assert.True(labelBinding > constructorStart);
        Assert.True(bodyBinding > labelBinding);
        Assert.True(headerBinding > bodyBinding);
        Assert.True(queryBinding > headerBinding);
    }

    [Fact]
    public void GenerateEmitsCollectionEnumUnionAndErrorShapes()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.weather#ForecastList": {
                  "type": "list",
                  "member": {
                    "target": "smithy.api#String"
                  }
                },
                "example.weather#ForecastTags": {
                  "type": "map",
                  "key": {
                    "target": "smithy.api#String"
                  },
                  "value": {
                    "target": "smithy.api#String"
                  },
                  "traits": {
                    "smithy.api#sparse": {}
                  }
                },
                "example.weather#WeatherKind": {
                  "type": "enum",
                  "members": {
                    "sunny": {
                      "traits": {
                        "smithy.api#enumValue": "SUN"
                      }
                    },
                    "rainy": {}
                  }
                },
                "example.weather#WeatherCode": {
                  "type": "intEnum",
                  "members": {
                    "ok": {
                      "traits": {
                        "smithy.api#enumValue": 1
                      }
                    }
                  }
                },
                "example.weather#ForecastValue": {
                  "type": "union",
                  "members": {
                    "text": {
                      "target": "smithy.api#String"
                    },
                    "code": {
                      "target": "example.weather#WeatherCode"
                    }
                  }
                },
                "example.weather#BadRequest": {
                  "type": "structure",
                  "traits": {
                    "smithy.api#error": "client"
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

        var files = new CSharpShapeGenerator()
            .Generate(model)
            .ToDictionary(file => file.Path, file => file.Contents);

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#ForecastList", ShapeKind.List)]
            public sealed partial record class ForecastList
            {
                public ForecastList(IEnumerable<string> values)
                {
                    ArgumentNullException.ThrowIfNull(values);
                    Values = Array.AsReadOnly(values.ToArray());
                }

                [SmithyMember("member", "smithy.api#String")]
                public IReadOnlyList<string> Values { get; }
            }
            """,
            files["Example/Weather/ForecastList.g.cs"]
        );

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#ForecastTags", ShapeKind.Map)]
            [SmithyTrait("smithy.api#sparse")]
            public sealed partial record class ForecastTags
            {
                public ForecastTags(IReadOnlyDictionary<string, string?> values)
                {
                    ArgumentNullException.ThrowIfNull(values);
                    Values = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(values));
                }

                [SmithyMember("value", "smithy.api#String", IsSparse = true)]
                public IReadOnlyDictionary<string, string?> Values { get; }
            }
            """,
            files["Example/Weather/ForecastTags.g.cs"]
        );

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#WeatherKind", ShapeKind.Enum)]
            public readonly partial record struct WeatherKind(string Value)
            {
                [SmithyEnumValue("rainy")]
                public static WeatherKind Rainy { get; } = new("rainy");
                [SmithyEnumValue("SUN")]
                public static WeatherKind Sunny { get; } = new("SUN");

                public override string ToString()
                {
                    return Value;
                }
            }
            """,
            files["Example/Weather/WeatherKind.g.cs"]
        );

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#WeatherCode", ShapeKind.IntEnum)]
            public enum WeatherCode
            {
                [SmithyEnumValue("1")]
                Ok = 1,
            }
            """,
            files["Example/Weather/WeatherCode.g.cs"]
        );

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#ForecastValue", ShapeKind.Union)]
            public abstract partial record class ForecastValue
            {
                private protected ForecastValue() { }

                [SmithyMember("code", "example.weather#WeatherCode")]
                public sealed partial record class Code : ForecastValue
                {
                    public Code(global::Example.Weather.WeatherCode value)
                    {
                        Value = value;
                    }

                    public global::Example.Weather.WeatherCode Value { get; }
                }

                public static ForecastValue FromCode(global::Example.Weather.WeatherCode value)
                {
                    return new Code(value);
                }

                [SmithyMember("text", "smithy.api#String")]
                public sealed partial record class Text : ForecastValue
                {
                    public Text(string value)
                    {
                        Value = value ?? throw new ArgumentNullException(nameof(value));
                    }

                    public string Value { get; }
                }

                public static ForecastValue FromText(string value)
                {
                    return new Text(value);
                }

                public sealed partial record class Unknown : ForecastValue
                {
                    public Unknown(string tag, Document value)
                    {
                        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
                        Value = value;
                    }

                    public string Tag { get; }
                    public Document Value { get; }
                }

                public static ForecastValue FromUnknown(string tag, Document value)
                {
                    return new Unknown(tag, value);
                }

                public T Match<T>(
                    Func<global::Example.Weather.WeatherCode, T> code,
                    Func<string, T> text,
                    Func<string, Document, T> unknown)
                {
                    ArgumentNullException.ThrowIfNull(code);
                    ArgumentNullException.ThrowIfNull(text);
                    ArgumentNullException.ThrowIfNull(unknown);

                    return this switch
                    {
                        Code value => code(value.Value),
                        Text value => text(value.Value),
                        Unknown value => unknown(value.Tag, value.Value),
                        _ => throw new InvalidOperationException("Unknown union variant."),
                    };
                }
            }
            """,
            files["Example/Weather/ForecastValue.g.cs"]
        );

        NormalizedTextAssert.Equal(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using SmithyNet.Core;
            using SmithyNet.Core.Annotations;

            namespace Example.Weather;

            [SmithyShape("example.weather#BadRequest", ShapeKind.Structure)]
            [SmithyTrait("smithy.api#error", Value = "client")]
            public sealed partial class BadRequest : Exception
            {
                public BadRequest(string? message = null, string? reason = null)
                    : base(message)
                {
                    Reason = reason;
                }

                [SmithyMember("message", "smithy.api#String")]
                public override string Message => base.Message;

                [SmithyMember("reason", "smithy.api#String")]
                public string? Reason { get; }
            }
            """,
            files["Example/Weather/BadRequest.g.cs"]
        );
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunDotNetBuild(
        string projectDirectory
    )
    {
        using var process = Process.Start(
            new ProcessStartInfo(
                "dotnet",
                ["build", "-p:UseSharedCompilation=false", "--disable-build-servers"]
            )
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

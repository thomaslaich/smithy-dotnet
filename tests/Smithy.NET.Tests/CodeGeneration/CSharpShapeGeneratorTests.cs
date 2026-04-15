using System.Diagnostics;
using Smithy.NET.CodeGeneration;
using Smithy.NET.CodeGeneration.CSharp;
using Smithy.NET.Tests.GoldenFiles;

namespace Smithy.NET.Tests.CodeGeneration;

public sealed class CSharpShapeGeneratorTests
{
    [Fact]
    public async Task GenerateEmitsCompilableCSharpForCrossShapeModel()
    {
        using var directory = TemporaryDirectory.Create();
        var projectDirectory = directory.Path;
        Directory.CreateDirectory(projectDirectory);

        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "shapes": {
                "example.common#Metadata": {
                  "type": "structure",
                  "members": {
                    "source": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    }
                  }
                },
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
                  }
                },
                "example.weather#WeatherKind": {
                  "type": "enum",
                  "members": {
                    "sunny": {
                      "traits": {
                        "smithy.api#enumValue": "SUN"
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
                    "metadata": {
                      "target": "example.common#Metadata"
                    }
                  }
                },
                "example.weather#ForecastRequest": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "kind": {
                      "target": "example.weather#WeatherKind",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "metadata": {
                      "target": "example.common#Metadata",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "summaries": {
                      "target": "example.weather#ForecastList",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "tags": {
                      "target": "example.weather#ForecastTags",
                      "traits": {
                        "smithy.api#required": {}
                      }
                    },
                    "value": {
                      "target": "example.weather#ForecastValue",
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
            Path.Combine(projectDirectory, "GeneratedCompileTest.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="{{FindRepositoryRoot()}}/src/Smithy.NET.Core/Smithy.NET.Core.csproj" />
              </ItemGroup>
            </Project>
            """
        );

        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Consumer.cs"),
            """
            using Example.Common;
            using Example.Weather;

            namespace GeneratedCompileTest;

            public static class Consumer
            {
                public static ForecastRequest Create()
                {
                    var metadata = new Metadata("station");
                    return new ForecastRequest(
                        "Zurich",
                        WeatherKind.Sunny,
                        metadata,
                        new ForecastList(["clear"]),
                        new ForecastTags(new Dictionary<string, string> { ["region"] = "ch" }),
                        new ForecastValue.Metadata(metadata)
                    );
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
        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public sealed partial record class ForecastRequest
            {
                public ForecastRequest(string city, int? days = null, string? units = null)
                {
                    City = city ?? throw new ArgumentNullException(nameof(city));
                    Days = days;
                    Units = units ?? "metric";
                }

                public string City { get; }
                public int? Days { get; }
                public string Units { get; }
            }
            """,
            file.Contents
        );
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

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public sealed partial record class ForecastList
            {
                public ForecastList(IEnumerable<string> values)
                {
                    ArgumentNullException.ThrowIfNull(values);
                    Values = Array.AsReadOnly(values.ToArray());
                }

                public IReadOnlyList<string> Values { get; }
            }
            """,
            files["Example/Weather/ForecastList.g.cs"]
        );

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public sealed partial record class ForecastTags
            {
                public ForecastTags(IReadOnlyDictionary<string, string?> values)
                {
                    ArgumentNullException.ThrowIfNull(values);
                    Values = new Dictionary<string, string?>(values);
                }

                public IReadOnlyDictionary<string, string?> Values { get; }
            }
            """,
            files["Example/Weather/ForecastTags.g.cs"]
        );

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public readonly partial record struct WeatherKind(string Value)
            {
                public static WeatherKind Rainy { get; } = new("rainy");
                public static WeatherKind Sunny { get; } = new("SUN");

                public override string ToString()
                {
                    return Value;
                }
            }
            """,
            files["Example/Weather/WeatherKind.g.cs"]
        );

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public enum WeatherCode
            {
                Ok = 1,
            }
            """,
            files["Example/Weather/WeatherCode.g.cs"]
        );

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public abstract partial record class ForecastValue
            {
                private protected ForecastValue() { }

                public sealed partial record class Code(WeatherCode Value) : ForecastValue;
                public sealed partial record class Text(string Value) : ForecastValue;

                public sealed partial record class Unknown(string Tag, Document Value) : ForecastValue;

                public T Match<T>(
                    Func<Code, T> code,
                    Func<Text, T> text,
                    Func<Unknown, T> unknown)
                {
                    return this switch
                    {
                        Code value => code(value),
                        Text value => text(value),
                        Unknown value => unknown(value),
                        _ => throw new InvalidOperationException("Unknown union variant."),
                    };
                }
            }
            """,
            files["Example/Weather/ForecastValue.g.cs"]
        );

        GoldenFile.AssertMatches(
            """
            // <auto-generated />
            #nullable enable

            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Smithy.NET.Core;

            namespace Example.Weather;

            public sealed partial class BadRequest : Exception
            {
                public BadRequest(string? message = null, string? reason = null)
                    : base(message)
                {
                    Reason = reason;
                }

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
            new ProcessStartInfo("dotnet", ["build"])
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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Smithy.NET.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the Smithy.NET repository root.");
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

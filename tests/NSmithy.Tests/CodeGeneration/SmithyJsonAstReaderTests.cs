using NSmithy.CodeGeneration;
using NSmithy.CodeGeneration.Model;
using NSmithy.Core;

namespace NSmithy.Tests.CodeGeneration;

public sealed class SmithyJsonAstReaderTests
{
    [Fact]
    public void ReadConvertsSmithyJsonAstIntoGenerationModel()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "smithy": "2.0",
              "metadata": {
                "suppressions": [
                  {
                    "id": "Example"
                  }
                ]
              },
              "shapes": {
                "example.weather#Weather": {
                  "type": "service",
                  "version": "2026-04-13",
                  "operations": [
                    "example.weather#GetForecast"
                  ],
                  "traits": {
                    "aws.protocols#restJson1": {}
                  }
                },
                "example.weather#GetForecast": {
                  "type": "operation",
                  "input": "example.weather#GetForecastInput",
                  "output": "example.weather#GetForecastOutput",
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
                        "smithy.api#required": {}
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
                },
                "example.weather#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String"
                    }
                  }
                },
                "example.weather#BadRequest": {
                  "type": "structure",
                  "traits": {
                    "smithy.api#error": "client"
                  }
                }
              }
            }
            """
        );

        var service = model.GetShape(ShapeId.Parse("example.weather#Weather"));
        var operation = model.GetShape(ShapeId.Parse("example.weather#GetForecast"));
        var input = model.GetShape(ShapeId.Parse("example.weather#GetForecastInput"));
        var error = model.GetShape(ShapeId.Parse("example.weather#BadRequest"));

        Assert.Equal("2.0", model.SmithyVersion);
        Assert.Single(model.Metadata["suppressions"].AsArray());
        Assert.Equal(ShapeKind.Service, service.Kind);
        Assert.Equal(ShapeId.Parse("example.weather#GetForecast"), service.Operations.Single());
        Assert.Equal(ShapeId.Parse("aws.protocols#restJson1"), service.Protocols.Single());
        Assert.Equal(ShapeId.Parse("example.weather#GetForecastInput"), operation.Input);
        Assert.Equal(ShapeId.Parse("example.weather#GetForecastOutput"), operation.Output);
        Assert.Equal(ShapeId.Parse("example.weather#BadRequest"), operation.Errors.Single());
        Assert.True(input.Members["city"].IsRequired);
        Assert.Equal("metric", input.Members["units"].DefaultValue?.AsString());
        Assert.True(input.Members["units"].IsClientOptional);
        Assert.True(error.Traits.Has(SmithyPrelude.ErrorTrait));
    }

    [Fact]
    public void ReadAcceptsWrappedBuildOutput()
    {
        var model = SmithyJsonAstReader.Read(
            """
            {
              "projection": "source",
              "model": {
                "smithy": "2.0",
                "shapes": {}
              }
            }
            """
        );

        Assert.Equal("2.0", model.SmithyVersion);
        Assert.Empty(model.Shapes);
    }
}

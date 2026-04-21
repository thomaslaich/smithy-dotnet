using SmithyNet.CodeGeneration;
using SmithyNet.CodeGeneration.Model;
using SmithyNet.CodeGeneration.Proto;
using SmithyNet.Tests.Assertions;

namespace SmithyNet.Tests.CodeGeneration;

public sealed class ProtoShapeGeneratorTests
{
    [Fact]
    public void GenerateEmitsUnaryGrpcProtoForAnnotatedService()
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
                      "target": "smithy.api#String"
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

        var file = Assert.Single(new ProtoShapeGenerator().Generate(model));

        Assert.Equal("example/weather/Weather.proto", file.Path);
        NormalizedTextAssert.Equal(
            """
            syntax = "proto3";
            option csharp_namespace = "Example.Weather.Grpc";

            package example.weather;

            message GetForecastInput {
              string city = 1;
              int32 days = 2;
            }

            message GetForecastOutput {
              string summary = 1;
            }

            service Weather {
              rpc GetForecast (GetForecastInput) returns (GetForecastOutput);
            }
            """,
            file.Contents
        );
    }

    [Fact]
    public void GenerateImportsGoogleProtobufEmptyWhenOperationUsesNoInputOrOutput()
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
                    "example.weather#Ping"
                  ]
                },
                "example.weather#Ping": {
                  "type": "operation"
                }
              }
            }
            """
        );

        var file = Assert.Single(new ProtoShapeGenerator().Generate(model));

        NormalizedTextAssert.Equal(
            """
            syntax = "proto3";
            option csharp_namespace = "Example.Weather.Grpc";
            import "google/protobuf/empty.proto";

            package example.weather;

            service Weather {
              rpc Ping (google.protobuf.Empty) returns (google.protobuf.Empty);
            }
            """,
            file.Contents
        );
    }
}

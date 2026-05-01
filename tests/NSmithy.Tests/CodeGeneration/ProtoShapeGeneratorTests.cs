using NSmithy.CodeGeneration;
using NSmithy.CodeGeneration.Model;
using NSmithy.CodeGeneration.Proto;
using NSmithy.Core;
using NSmithy.Tests.Assertions;

namespace NSmithy.Tests.CodeGeneration;

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
                      "target": "smithy.api#String",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    },
                    "days": {
                      "target": "smithy.api#Integer",
                      "traits": {
                        "alloy.proto#protoIndex": 2
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
                        "alloy.proto#protoIndex": 1
                      }
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
              optional string city = 1;
              optional int32 days = 2;
            }

            message GetForecastOutput {
              optional string summary = 1;
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

    [Fact]
    public void GenerateRequiresExplicitProtoFieldNumbersForMessageMembers()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        var exception = Assert.Throws<SmithyException>(() =>
            new ProtoShapeGenerator().Generate(model)
        );
        Assert.Contains("alloy.proto#protoIndex", exception.Message);
        Assert.Contains("example.weather#GetForecastInput$city", exception.Message);
    }

    [Fact]
    public void GenerateEmitsCollectionsEnumsAndQualifiedCrossNamespaceTypes()
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
                    "target": "example.common#GetForecastOutput"
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "cities": {
                      "target": "example.weather#CityList",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    },
                    "tags": {
                      "target": "example.weather#TagMap",
                      "traits": {
                        "alloy.proto#protoIndex": 2
                      }
                    },
                    "unit": {
                      "target": "example.common#TemperatureUnit",
                      "traits": {
                        "alloy.proto#protoIndex": 3
                      }
                    }
                  }
                },
                "example.common#GetForecastOutput": {
                  "type": "structure",
                  "members": {
                    "summary": {
                      "target": "smithy.api#String",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                },
                "example.weather#CityList": {
                  "type": "list",
                  "member": {
                    "target": "smithy.api#String"
                  }
                },
                "example.weather#TagMap": {
                  "type": "map",
                  "key": {
                    "target": "smithy.api#String"
                  },
                  "value": {
                    "target": "smithy.api#String"
                  }
                },
                "example.common#TemperatureUnit": {
                  "type": "enum",
                  "members": {
                    "CELSIUS": {},
                    "FAHRENHEIT": {}
                  }
                }
              }
            }
            """
        );

        var file = Assert.Single(new ProtoShapeGenerator().Generate(model));

        Assert.Contains("repeated string cities = 1;", file.Contents);
        Assert.Contains("map<string, string> tags = 2;", file.Contents);
        Assert.Contains("optional example.common.TemperatureUnit unit = 3;", file.Contents);
        Assert.Contains("message GetForecastOutput {", file.Contents);
        Assert.Contains("enum TemperatureUnit {", file.Contents);
    }

    [Fact]
    public void GeneratePreservesIntEnumValues()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "code": {
                      "target": "example.weather#WeatherCode",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                },
                "example.weather#WeatherCode": {
                  "type": "intEnum",
                  "members": {
                    "UNKNOWN": {
                      "traits": {
                        "smithy.api#enumValue": 0
                      }
                    },
                    "SUNNY": {
                      "traits": {
                        "smithy.api#enumValue": 10
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var file = Assert.Single(new ProtoShapeGenerator().Generate(model));
        Assert.Contains("UNKNOWN = 0;", file.Contents);
        Assert.Contains("SUNNY = 10;", file.Contents);
    }

    [Fact]
    public void GenerateRejectsDuplicateProtoFieldNumbers()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "city": {
                      "target": "smithy.api#String",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    },
                    "days": {
                      "target": "smithy.api#Integer",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var exception = Assert.Throws<SmithyException>(() =>
            new ProtoShapeGenerator().Generate(model)
        );
        Assert.Contains("duplicate alloy.proto#protoIndex value '1'", exception.Message);
    }

    [Fact]
    public void GenerateRejectsUnionShapes()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "value": {
                      "target": "example.weather#ForecastValue",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                },
                "example.weather#ForecastValue": {
                  "type": "union",
                  "members": {
                    "text": {
                      "target": "smithy.api#String"
                    }
                  }
                }
              }
            }
            """
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            new ProtoShapeGenerator().Generate(model)
        );
        Assert.Contains("Union", exception.Message);
    }

    [Fact]
    public void GenerateRejectsTimestampShapes()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "at": {
                      "target": "smithy.api#Timestamp",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            new ProtoShapeGenerator().Generate(model)
        );
        Assert.Contains("Timestamp", exception.Message);
    }

    [Fact]
    public void GenerateRejectsDocumentShapes()
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
                  }
                },
                "example.weather#GetForecastInput": {
                  "type": "structure",
                  "members": {
                    "payload": {
                      "target": "smithy.api#Document",
                      "traits": {
                        "alloy.proto#protoIndex": 1
                      }
                    }
                  }
                }
              }
            }
            """
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            new ProtoShapeGenerator().Generate(model)
        );
        Assert.Contains("Document", exception.Message);
    }
}

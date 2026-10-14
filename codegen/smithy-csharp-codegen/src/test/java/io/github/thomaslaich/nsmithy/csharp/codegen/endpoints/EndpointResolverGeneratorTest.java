package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

class EndpointResolverGeneratorTest {

  @TempDir Path tempDir;

  private static final String RULES =
      """
      $version: "2"
      metadata suppressions = [{ id: "UnstableTrait", namespace: "example.weather" }]
      namespace example.weather

      use smithy.rules#clientContextParams
      use smithy.rules#contextParam
      use smithy.rules#endpointRuleSet
      use smithy.rules#operationContextParams
      use smithy.rules#staticContextParams

      @clientContextParams(
          Region: { type: "string", documentation: "The region." }
          UseFips: { type: "boolean" }
      )
      @endpointRuleSet({
          version: "1.0"
          parameters: {
              Region: { type: "String", required: false }
              UseFips: { type: "Boolean", required: true, default: false }
              Endpoint: { type: "String", required: false, builtIn: "SDK::Endpoint" }
              City: { type: "String", required: false }
              Cities: { type: "stringArray", required: false }
          }
          rules: [
              {
                  conditions: [{ fn: "isSet", argv: [{ ref: "Endpoint" }] }]
                  type: "endpoint"
                  endpoint: { url: { ref: "Endpoint" } }
              }
              {
                  conditions: [
                      { fn: "isSet", argv: [{ ref: "Region" }] }
                      { fn: "parseURL", argv: ["https://{Region}.example.com"], assign: "url" }
                  ]
                  type: "endpoint"
                  endpoint: {
                      url: "https://{url#authority}/{Region}"
                      headers: { "x-city": ["{Region}"] }
                      properties: { authSchemes: [{ name: "sigv4", signingName: "weather" }] }
                  }
              }
              { conditions: [], type: "error", error: "Region must be set" }
          ]
      })
      service Weather {
          version: "2024-01-01"
          operations: [GetForecast, GetCapital, GetCities]
      }

      operation GetForecast {
          input := {
              @contextParam(name: "City")
              city: String
          }
      }

      @staticContextParams(City: { value: "Bern" })
      operation GetCapital {
          input := {}
      }

      @operationContextParams(Cities: { path: "stops[*].name" })
      operation GetCities {
          input := { stops: Stops }
      }

      list Stops { member: Stop }
      structure Stop { name: String }
      """;

  @Test
  void ruleSetCompilesToABddWalk() throws Exception {
    String generated = render(RULES);

    assertTrue(generated.contains("public sealed record WeatherEndpointParameters"), generated);
    assertTrue(generated.contains("public bool UseFips { get; init; } = false;"), generated);
    assertTrue(
        generated.contains("private static ReadOnlySpan<int> Nodes => new int[]"), generated);
    assertTrue(generated.contains("// isSet(Endpoint)"), generated);
    assertTrue(
        generated.contains(
            "(url = EndpointFunctions.ParseUrl($\"https://{region!}.example.com\"))"),
        generated);
    assertTrue(generated.contains("new Uri($\"https://{url!.Authority}/{region!}\")"), generated);
    assertTrue(
        generated.contains("throw new SmithyEndpointResolutionException(\"Region must be set\")"),
        generated);
  }

  @Test
  void resultsCarryHeadersAndAuthSchemes() throws Exception {
    String generated = render(RULES);

    assertTrue(generated.contains("[\"x-city\"] = new string[] { region! }"), generated);
    assertTrue(
        generated.contains(
            "new SmithyEndpointAuthScheme(\"sigv4\", new Dictionary<string, Document> {"
                + " [\"signingName\"] = Document.From(\"weather\") })"),
        generated);
  }

  @Test
  void operationsBindStaticContextAndOperationContextParameters() throws Exception {
    String generated = render(RULES);

    assertTrue(
        generated.contains("\"GetForecast\" when parameters.Input is GetForecastInput input"),
        generated);
    assertTrue(generated.contains("City = input.City ?? clientParameters.City"), generated);
    assertTrue(generated.contains("City = \"Bern\""), generated);
    assertTrue(generated.contains(".Select(item1 => item1.Name).OfType<string>()"), generated);
    // Per-operation bindings mean nothing is resolved at construction.
    assertFalse(generated.contains("StaticEndpoint = Resolve(clientParameters);"), generated);
  }

  private String render(String source) throws Exception {
    Model model =
        Model.assembler()
            .discoverModels(getClass().getClassLoader())
            .addUnparsedModel("model.smithy", source)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.weather#Weather"))
                .withMember("baseNamespace", Node.from(""))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(Files.createTempDirectory(tempDir, "manifest"));
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbolProvider)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider, model, settings))
            .build();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    var service = model.expectShape(ShapeId.from("example.weather#Weather"), ServiceShape.class);
    EndpointRules rules = EndpointRules.of(context, service).orElseThrow();

    new EndpointResolverGenerator(context, writer, rules).run();

    return writer.toString();
  }
}

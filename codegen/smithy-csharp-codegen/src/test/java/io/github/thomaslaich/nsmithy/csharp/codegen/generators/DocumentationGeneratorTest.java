package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Files;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;

final class DocumentationGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String REST_PROTOCOL_TRAITS =
      """
      $version: "2"

      namespace aws.protocols

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure restJson1 {}
      """;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.weather

      use aws.protocols#restJson1
      use smithy.api#http

      /// Weather service docs.
      @restJson1
      service Weather {
          version: "1"
          operations: [GetForecast]
      }

      /// Gets <forecast> & conditions.
      @http(method: "POST", uri: "/forecast")
      operation GetForecast {
          input: GetForecastInput
          output: GetForecastOutput
          errors: [ForecastError]
      }

      /// Forecast input docs.
      structure GetForecastInput {
          /// City <name> & region.
          @required
          city: String
      }

      structure GetForecastOutput {}

      structure ParamOnlyDocs {
          /// Member-only docs.
          value: String
      }

      /// Forecast error docs.
      @error("client")
      structure ForecastError {
          /// Error message docs.
          message: String
      }

      /// Weather condition docs.
      enum Condition {
          /// Sunny docs.
          SUNNY
      }

      /// Alert level docs.
      intEnum AlertLevel {
          /// Low docs.
          LOW = 1
      }
      """;

  @Test
  void structuresEmitSummaryAndConstructorParamDocs() throws Exception {
    String generated = renderStructure("example.weather#GetForecastInput");

    assertTrue(generated.contains("/// <summary>\n/// Forecast input docs.\n/// </summary>"));
    assertTrue(
        generated.contains("/// <param name=\"City\">\n/// City &lt;name&gt; &amp; region."));
    assertTrue(
        generated.contains("public sealed record class GetForecastInput(string City);"), generated);
  }

  @Test
  void structuresUseFallbackSummaryForMemberDocsWithoutShapeDocs() throws Exception {
    String generated = renderStructure("example.weather#ParamOnlyDocs");

    assertTrue(
        generated.contains("/// Represents the Smithy structure example.weather#ParamOnlyDocs."),
        generated);
    assertTrue(generated.contains("/// <param name=\"Value\">"), generated);
    assertTrue(generated.contains("/// Member-only docs."), generated);
    assertTrue(
        generated.contains("public sealed record class ParamOnlyDocs(string? Value = null);"));
  }

  @Test
  void enumsEmitTypeAndVariantDocs() throws Exception {
    String stringEnum = renderStringEnum("example.weather#Condition");
    String intEnum = renderIntEnum("example.weather#AlertLevel");

    assertTrue(stringEnum.contains("/// Weather condition docs."), stringEnum);
    assertTrue(stringEnum.contains("/// Sunny docs."), stringEnum);
    assertTrue(intEnum.contains("/// Alert level docs."), intEnum);
    assertTrue(intEnum.contains("/// Low docs."), intEnum);
  }

  @Test
  void errorsEmitTypeConstructorAndMemberDocs() throws Exception {
    String generated = renderError("example.weather#ForecastError");

    assertTrue(generated.contains("/// Forecast error docs."), generated);
    assertTrue(generated.contains("/// <param name=\"message\">"), generated);
    assertTrue(generated.contains("/// Error message docs."), generated);
    assertTrue(generated.contains("public override string Message"), generated);
  }

  @Test
  void clientsAndServersEmitServiceAndOperationDocs() throws Exception {
    String client = renderClient();
    String server = renderServer();

    assertTrue(
        client.contains(
            "/// Weather service docs.\n/// </summary>\npublic interface IWeatherClient"));
    assertTrue(client.contains("/// Gets &lt;forecast&gt; &amp; conditions."));
    assertTrue(client.contains("/// <param name=\"input\">"), client);
    assertTrue(client.contains("/// Forecast input docs."), client);
    assertTrue(
        server.contains(
            "/// Weather service docs.\n/// </summary>\npublic interface IWeatherServiceHandler"));
    assertTrue(server.contains("/// Gets &lt;forecast&gt; &amp; conditions."));
  }

  private String renderStructure(String shapeId) throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new StructureGenerator(
            context, writer, model.expectShape(ShapeId.from(shapeId), StructureShape.class))
        .run();
    return writer.toString();
  }

  private String renderStringEnum(String shapeId) throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new StringEnumGenerator(writer, model.expectShape(ShapeId.from(shapeId), EnumShape.class))
        .run();
    return writer.toString();
  }

  private String renderIntEnum(String shapeId) throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new IntEnumGenerator(writer, model.expectShape(ShapeId.from(shapeId), IntEnumShape.class))
        .run();
    return writer.toString();
  }

  private String renderError(String shapeId) throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new ErrorGenerator(
            context, writer, model.expectShape(ShapeId.from(shapeId), StructureShape.class))
        .run();
    return writer.toString();
  }

  private String renderClient() throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new ClientGenerator(
            context,
            writer,
            model.expectShape(ShapeId.from("example.weather#Weather"), ServiceShape.class))
        .run();
    return writer.toString();
  }

  private String renderServer() throws Exception {
    var model = model();
    var context = context(model);
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    new ServerGenerator(
            context,
            writer,
            model.expectShape(ShapeId.from("example.weather#Weather"), ServiceShape.class))
        .run();
    return writer.toString();
  }

  private Model model() {
    return Model.assembler()
        .addUnparsedModel("protocol-traits.smithy", REST_PROTOCOL_TRAITS)
        .addUnparsedModel("model.smithy", MODEL)
        .assemble()
        .unwrap();
  }

  private GenerationContext context(Model model) throws Exception {
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.weather#Weather"))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(Files.createTempDirectory(tempDir, "manifest"));
    return GenerationContext.builder()
        .model(model)
        .settings(settings)
        .symbolProvider(symbolProvider)
        .fileManifest(manifest)
        .writerDelegator(new CSharpDelegator(manifest, symbolProvider, model, settings))
        .build();
  }
}

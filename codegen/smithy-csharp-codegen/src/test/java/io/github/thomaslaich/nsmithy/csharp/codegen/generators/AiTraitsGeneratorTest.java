package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.net.URL;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Objects;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.validation.ValidatedResult;

final class AiTraitsGeneratorTest {

  private static final String SERVICE_ID = "example.weather#WeatherService";

  @TempDir Path tempDir;

  @Test
  void preservesPromptsOnServiceAndOperationRuntimeSchemas() throws Exception {
    Model model = assemble("ai/prompts.smithy").unwrap();
    var service = model.expectShape(ShapeId.from(SERVICE_ID), ServiceShape.class);
    var operation =
        model.expectShape(ShapeId.from("example.weather#GetForecast"), OperationShape.class);

    String serviceSchema = renderServiceSchema(service);
    String operationSchema = renderOperationSchema(model, operation);

    assertTrue(
        serviceSchema.contains("new Trait(ShapeId.Parse(\"smithy.ai#prompts\")"), serviceSchema);
    assertTrue(serviceSchema.contains("\"weather_brief\""), serviceSchema);
    assertTrue(serviceSchema.contains("\"Create a concise weather brief\""), serviceSchema);
    assertTrue(serviceSchema.contains("\"example.weather#WeatherBriefArguments\""), serviceSchema);
    assertTrue(serviceSchema.contains("\"The user wants a short weather summary\""), serviceSchema);

    assertTrue(
        operationSchema.contains("new Trait(ShapeId.Parse(\"smithy.ai#prompts\")"),
        operationSchema);
    assertTrue(operationSchema.contains("\"forecast_for_location\""), operationSchema);
    assertTrue(operationSchema.contains("\"Get the forecast for one location\""), operationSchema);
    assertTrue(operationSchema.contains("\"example.weather#GetForecastInput\""), operationSchema);
    assertFalse(operationSchema.contains("OperationJsonSchemas"), operationSchema);
  }

  @Test
  void rejectsPromptNamesThatOnlyDifferByCase() {
    ValidatedResult<Model> result = assemble("ai/duplicate-prompts.smithy");

    assertTrue(result.isBroken(), result.getValidationEvents().toString());
    assertTrue(
        result.getValidationEvents().stream()
            .anyMatch(
                event ->
                    event
                        .getMessage()
                        .contains(
                            "Duplicate prompt name detected: 'Weather_Report' conflicts with an"
                                + " existing prompt")),
        result.getValidationEvents().toString());
  }

  private ValidatedResult<Model> assemble(String resourceName) {
    return Model.assembler()
        .discoverModels(getClass().getClassLoader())
        .addImport(resource(resourceName))
        .assemble();
  }

  private URL resource(String resourceName) {
    return Objects.requireNonNull(
        getClass().getClassLoader().getResource(resourceName),
        "Missing test resource " + resourceName);
  }

  private static String renderServiceSchema(ServiceShape service) {
    var writer = new CSharpWriter("Example.Weather");
    new ServiceSchemaGenerator(writer, service).run();
    return writer.toString();
  }

  private String renderOperationSchema(Model model, OperationShape operation) throws Exception {
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from(SERVICE_ID))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(Files.createDirectory(tempDir.resolve("manifest")));
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbolProvider)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider))
            .build();
    var writer = new CSharpWriter("Example.Weather");

    new OperationSchemaGenerator(context, writer, operation).run();

    return writer.toString();
  }
}

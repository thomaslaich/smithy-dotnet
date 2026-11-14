package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

/**
 * {@code smithy.framework#ValidationException}, which a model declares on the operations whose
 * input the server validates: listed in their schemas, bound to the runtime's type, never
 * generated.
 */
class ValidationExceptionIntegrationTest {

  private static final String MODEL =
      """
      $version: "2"
      namespace example.cafe

      use smithy.framework#ValidationException

      service Cafe {
          version: "2024-01-01"
          operations: [Order, Browse]
      }

      operation Order {
          input := {
              @required
              dish: String
          }
          errors: [SoldOut, ValidationException]
      }

      operation Browse {
          input := { page: Integer }
      }

      @error("client")
      structure SoldOut {}
      """;

  /** The same model without the declaration. */
  private static final String UNDECLARED =
      MODEL
          .replace("use smithy.framework#ValidationException\n", "")
          .replace("errors: [SoldOut, ValidationException]", "errors: [SoldOut]");

  @Test
  void aConstrainedOperationsSchemaListsTheRuntimesErrorSchema() {
    MockManifest manifest = generate(settings());

    String order = schema(manifest, "Order");
    assertTrue(
        order.contains(
            "Schemas.OperationError(ShapeId.Parse(\"smithy.framework#ValidationException\"),"
                + " global::NSmithy.Core.Serde.Schemas.Lazy(() =>"
                + " ValidationExceptionSchema.Schema!), 400)"),
        order);
    assertTrue(order.contains("example.cafe#SoldOut"), order);
    assertFalse(schema(manifest, "Browse").contains("ValidationException"));
  }

  @Test
  void theFrameworkShapesAreNotGenerated() {
    MockManifest manifest = generate(settings());

    assertTrue(
        manifest.getFiles().stream()
            .noneMatch(file -> file.toString().contains("Smithy/Framework")),
        manifest.getFiles().toString());
  }

  @Test
  void aClientFollowsAModelThatDoesNotDeclareTheError() {
    MockManifest manifest = generate(UNDECLARED, settings().withMember("generateServer", false));

    assertFalse(schema(manifest, "Order").contains("ValidationException"));
  }

  @Test
  void aServerRequiresTheModelToDeclareTheError() {
    CodegenException error =
        assertThrows(CodegenException.class, () -> generate(UNDECLARED, settings()));

    assertTrue(
        error.getMessage().startsWith("Operation example.cafe#Order takes input"),
        error.getMessage());
  }

  @Test
  void attachingDeclaresTheErrorForAModelThatDoesNot() {
    MockManifest manifest =
        generate(UNDECLARED, settings().withMember("attachValidationException", true));

    assertTrue(schema(manifest, "Order").contains("smithy.framework#ValidationException"));
    assertFalse(schema(manifest, "Browse").contains("ValidationException"));
  }

  private static String schema(MockManifest manifest, String operation) {
    return manifest
        .expectFileString(Path.of("Example/Cafe/" + operation + ".Schema.g.cs"))
        .replaceAll("\\s+", " ");
  }

  private static ObjectNode.Builder settings() {
    return Node.objectNodeBuilder().withMember("service", "example.cafe#Cafe");
  }

  private static MockManifest generate(ObjectNode.Builder settings) {
    return generate(MODEL, settings);
  }

  private static MockManifest generate(String source, ObjectNode.Builder settings) {
    Model model =
        Model.assembler()
            .discoverModels() // the validation model's shapes
            .addUnparsedModel("cafe.smithy", source)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(
        model, CSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }
}

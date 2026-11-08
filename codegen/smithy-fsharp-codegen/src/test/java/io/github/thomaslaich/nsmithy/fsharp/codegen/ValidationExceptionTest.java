package io.github.thomaslaich.nsmithy.fsharp.codegen;

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
 * {@code smithy.framework#ValidationException} on the operations whose input the server validates:
 * a case of the operation's error union, bound to the runtime's type.
 */
class ValidationExceptionTest {

  private static final Path TYPES = Path.of("Types.g.fs");
  private static final Path SCHEMAS = Path.of("Schemas.g.fs");
  private static final Path OPERATIONS = Path.of("Operations.g.fs");
  private static final Path CLIENT = Path.of("Client.g.fs");

  private static final String VALIDATION_EXCEPTION =
      "global.NSmithy.Core.Validation.ValidationException";

  private static final String MODEL =
      """
      $version: "2"
      namespace example.cafe

      use aws.protocols#restJson1

      @restJson1
      service Cafe {
          version: "2024-01-01"
          operations: [Order, Browse, Ping]
      }

      /// A constrained input and an error of its own.
      @http(method: "POST", uri: "/orders")
      operation Order {
          input := {
              @required
              @length(min: 1)
              dish: String
          }
          output := {
              @required
              id: String
          }
          errors: [SoldOut]
      }

      /// An input the server never validates, and no errors.
      @http(method: "GET", uri: "/menu")
      operation Browse {
          input := {
              @httpQuery("page")
              page: Integer
          }
          output := {
              dishes: Dishes
          }
      }

      @http(method: "GET", uri: "/ping")
      operation Ping {}

      list Dishes {
          member: String
      }

      @error("client")
      @httpError(409)
      structure SoldOut {}
      """;

  /** A stand-in for the AWS traits package, which defines the real trait. */
  private static final String REST_JSON_1 =
      """
      $version: "2"
      namespace aws.protocols

      @trait(selector: "service")
      @protocolDefinition
      structure restJson1 {}
      """;

  @Test
  void aConstrainedOperationReturnsTheErrorAsACase() {
    String client = generate(settings()).expectFileString(CLIENT);

    assertTrue(
        client.contains(
            "type OrderError =\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut\n"
                + "    | ValidationException of "
                + VALIDATION_EXCEPTION),
        client);
    assertTrue(
        client.contains(
            "| :? "
                + VALIDATION_EXCEPTION
                + " as error -> return"
                + " global.Microsoft.FSharp.Core.Result.Error(OrderError.ValidationException"
                + " error)"),
        client);
  }

  @Test
  void anOperationTheServerDoesNotValidateIsLeftAlone() {
    String client = generate(settings()).expectFileString(CLIENT);

    assertFalse(client.contains("type BrowseError"), client);
    assertFalse(client.contains("type PingError"), client);
    assertTrue(
        client.contains(
            "abstract BrowseAsync: input: global.Example.Cafe.BrowseInput * ?cancellationToken:"
                + " global.System.Threading.CancellationToken ->"
                + " global.System.Threading.Tasks.Task<global.Example.Cafe.BrowseOutput>"),
        client);
  }

  @Test
  void theOperationSchemaListsTheRuntimesErrorSchema() {
    String operations = generate(settings()).expectFileString(OPERATIONS);

    assertTrue(
        operations.contains(
            "global.NSmithy.Core.Serde.Schemas.OperationError<"
                + VALIDATION_EXCEPTION
                + ">(global.NSmithy.Core.ShapeId.Parse(\"smithy.framework#ValidationException\"),"
                + " global.NSmithy.Core.Validation.ValidationExceptionSchema.Schema, 400)"),
        operations);
  }

  @Test
  void theFrameworkShapesAreNotGenerated() {
    MockManifest manifest = generate(settings());

    String types = manifest.expectFileString(TYPES);
    assertFalse(types.contains("ValidationException"), types);
    assertFalse(types.contains("Smithy.Framework"), types);
    String schemas = manifest.expectFileString(SCHEMAS);
    assertFalse(schemas.contains("Smithy.Framework"), schemas);
  }

  @Test
  void attachingCanBeTurnedOffForAClient() {
    String client =
        generate(
                settings()
                    .withMember("attachValidationException", false)
                    .withMember("generateServer", false))
            .expectFileString(CLIENT);

    assertTrue(
        client.contains("type OrderError =\n    | SoldOut of global.Example.Cafe.SoldOut\n"),
        client);
    assertFalse(client.contains("ValidationException"), client);
  }

  @Test
  void aServerThatDoesNotAttachMustDeclareTheError() {
    CodegenException error =
        assertThrows(
            CodegenException.class,
            () -> generate(settings().withMember("attachValidationException", false)));

    assertTrue(
        error.getMessage().startsWith("Operation example.cafe#Order takes input"),
        error.getMessage());
  }

  private static ObjectNode.Builder settings() {
    return Node.objectNodeBuilder().withMember("service", "example.cafe#Cafe");
  }

  private static MockManifest generate(ObjectNode.Builder settings) {
    Model model =
        Model.assembler()
            .addUnparsedModel("rest-json-1.smithy", REST_JSON_1)
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    FSharpCodegenPlugin.generate(
        model, FSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }
}

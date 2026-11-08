package io.github.thomaslaich.nsmithy.fsharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;
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

class ClientGeneratorTest {

  private static final Path CLIENT = Path.of("Client.g.fs");
  private static final Path SCHEMAS = Path.of("Schemas.g.fs");
  private static final Path OPERATIONS = Path.of("Operations.g.fs");

  private static final String MODEL =
      """
      $version: "2"
      namespace example.cafe

      use aws.protocols#restJson1

      @restJson1
      service Cafe {
          version: "2024-01-01"
          operations: [Order, Ping, Forget]
      }

      @http(method: "POST", uri: "/orders")
      operation Order {
          input := {
              @idempotencyToken
              token: String
          }
          output := {
              @required
              id: String
          }
          errors: [SoldOut]
      }

      @http(method: "GET", uri: "/ping")
      operation Ping {
          input: Unit
          output: Unit
      }

      @http(method: "DELETE", uri: "/orders")
      operation Forget {}

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
  void generatesAnInterfaceAConfigAndAClient() {
    String client = generate(MODEL, true).expectFileString(CLIENT);

    assertTrue(client.contains("namespace Example.Cafe"), client);
    assertTrue(
        client.contains("type ICafeClient =\n    inherit global.System.IDisposable"), client);
    assertTrue(
        client.contains(
            "abstract OrderAsync: input: global.Example.Cafe.OrderInput * ?cancellationToken:"
                + " global.System.Threading.CancellationToken ->"
                + " global.System.Threading.Tasks.Task<global.Microsoft.FSharp.Core.Result<"
                + "global.Example.Cafe.OrderOutput, OrderError>>"),
        client);
    assertTrue(client.contains("type CafeClientConfig ="), client);
    assertTrue(client.contains("global.NSmithy.Protocols.RestJson.RestJson1Protocol()"), client);
    assertTrue(client.contains("interface ICafeClient with"), client);
  }

  @Test
  void anOperationWithoutInputOrOutputTakesItsSynthesizedInput() {
    String client = generate(MODEL, true).expectFileString(CLIENT);

    assertTrue(
        client.contains(
            "abstract PingAsync: input: global.Example.Cafe.PingInput * ?cancellationToken:"
                + " global.System.Threading.CancellationToken ->"
                + " global.System.Threading.Tasks.Task<global.Example.Cafe.PingOutput>"),
        client);
    // Its input needs nothing, so the client and its interface also take only the token.
    assertTrue(
        client.contains(
            "member this.PingAsync(?cancellationToken: global.System.Threading.CancellationToken) :"
                + " global.System.Threading.Tasks.Task<global.Example.Cafe.PingOutput> ="),
        client);
    assertTrue(
        client.contains(
            "this.PingAsync(global.Example.Cafe.PingInput(), ?cancellationToken ="
                + " cancellationToken)"),
        client);
    assertTrue(
        client.contains("module ICafeClientExtensions =\n    type ICafeClient with"), client);
    // A record input whose members are all optional is built with every member absent.
    assertTrue(
        client.contains(
            "this.OrderAsync(({ Token = None } : global.Example.Cafe.OrderInput),"
                + " ?cancellationToken = cancellationToken)"),
        client);
  }

  @Test
  void fillsInAMissingIdempotencyToken() {
    String client = generate(MODEL, true).expectFileString(CLIENT);

    assertTrue(client.contains("config.IdempotencyTokenProvider"), client);
    assertTrue(client.contains("Token = (match input.Token with"), client);
  }

  @Test
  void anOperationSchemaListsItsErrorsWithTheirStatusCodes() {
    MockManifest manifest = generate(MODEL, true);
    String operations = manifest.expectFileString(OPERATIONS);

    assertTrue(operations.contains("namespace rec Example.Cafe.Schemas"), operations);
    assertTrue(operations.contains("type OrderSchema ="), operations);
    assertTrue(
        operations.contains("global.NSmithy.Core.ShapeId.Parse(\"example.cafe#SoldOut\")"),
        operations);
    assertTrue(
        operations.contains(", 409) :> global.NSmithy.Core.Serde.IOperationErrorSchema"),
        operations);
    assertTrue(manifest.expectFileString(SCHEMAS).contains("type CafeSchema ="));
  }

  @Test
  void anOperationThatDeclaresErrorsReturnsAResult() {
    String client = generate(MODEL, true).expectFileString(CLIENT);

    assertTrue(
        client.contains(
            "[<RequireQualifiedAccess>]\ntype OrderError =\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut"),
        client);
    assertTrue(
        client.contains(
            "| :? global.Example.Cafe.SoldOut as error -> return"
                + " global.Microsoft.FSharp.Core.Result.Error(OrderError.SoldOut error)"),
        client);
    assertFalse(client.contains("type PingError"), client);
  }

  private static final String SERVICE_ERRORS =
      MODEL.replace(
              "operations: [Order, Ping, Forget]",
              "operations: [Order, Ping, Forget]\n    errors: [Busy]")
          + "\n@error(\"server\")\n@httpError(503)\nstructure Busy {}\n";

  @Test
  void aServiceErrorIsTheServiceCaseOfEveryOperation() {
    MockManifest manifest = generate(SERVICE_ERRORS, true);
    String client = manifest.expectFileString(CLIENT);

    assertTrue(
        client.contains(
            "[<RequireQualifiedAccess>]\ntype CafeError =\n    | Busy of global.Example.Cafe.Busy"),
        client);
    assertTrue(client.contains("type PingError =\n    | Service of CafeError"), client);
    assertTrue(
        client.contains(
            "type OrderError =\n    | SoldOut of global.Example.Cafe.SoldOut\n"
                + "    | Service of CafeError"),
        client);
    assertTrue(
        client.contains(
            "| :? global.Example.Cafe.Busy as error -> return"
                + " global.Microsoft.FSharp.Core.Result.Error(OrderError.Service(CafeError.Busy"
                + " error))"),
        client);
    assertTrue(client.contains("global.Example.Cafe.PingOutput, PingError>>"), client);
    // The service schema lists the service's errors once; the protocols add them to every
    // operation (ServiceSchema.ErrorsOf), so operation schemas list only their own.
    String schemas = manifest.expectFileString(SCHEMAS);
    assertEquals(
        1, schemas.split("OperationError<global.Example.Cafe.Busy", -1).length - 1, schemas);
    assertTrue(schemas.contains("errors ="), schemas);
    assertFalse(manifest.expectFileString(OPERATIONS).contains("Busy"));
  }

  @Test
  void anErrorOnBothTheOperationAndTheServiceIsTheOperations() {
    String model = SERVICE_ERRORS.replace("errors: [SoldOut]", "errors: [SoldOut, Busy]");

    String client = generate(model, true).expectFileString(CLIENT);

    assertTrue(
        client.contains(
            "type OrderError =\n    | Busy of global.Example.Cafe.Busy\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut\n"
                + "    | Service of CafeError"),
        client);
    assertTrue(client.contains("Result.Error(OrderError.Busy error)"), client);
    assertFalse(client.contains("OrderError.Service(CafeError.Busy"), client);
  }

  @Test
  void aShapeNamedLikeAnErrorTypeFails() {
    String model = MODEL + "\nstructure OrderError {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#OrderError"), error.getMessage());
  }

  @Test
  void aShapeNamedLikeTheServiceErrorTypeFails() {
    String model = SERVICE_ERRORS + "\nstructure CafeError {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#CafeError"), error.getMessage());
  }

  @Test
  void anErrorNamedServiceFails() {
    String model =
        SERVICE_ERRORS.replace("errors: [SoldOut]", "errors: [SoldOut, Service]")
            + "\n@error(\"client\")\nstructure Service {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#Service"), error.getMessage());
  }

  @Test
  void generateClientFalseLeavesTheClientOut() {
    MockManifest manifest = generate(MODEL, false);

    assertFalse(manifest.hasFile(CLIENT));
    assertTrue(manifest.hasFile(SCHEMAS));
  }

  @Test
  void aServiceWithoutAKnownProtocolGetsTheInterfaceOnly() {
    String client = generate(MODEL.replace("@restJson1\n", ""), true).expectFileString(CLIENT);

    assertTrue(client.contains("type ICafeClient ="), client);
    assertFalse(client.contains("type CafeClient "), client);
  }

  @Test
  void aShapeNamedLikeTheClientFails() {
    String model = MODEL + "\nstructure CafeClient {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#CafeClient"), error.getMessage());
  }

  private static MockManifest generate(String model, boolean generateClient) {
    Model assembled =
        Model.assembler()
            .addUnparsedModel("rest-json-1.smithy", REST_JSON_1)
            .addUnparsedModel("model.smithy", model)
            .assemble()
            .unwrap();
    ObjectNode settings =
        Node.objectNodeBuilder()
            .withMember("service", "example.cafe#Cafe")
            .withMember("generateClient", generateClient)
            .build();
    MockManifest manifest = new MockManifest();
    FSharpCodegenPlugin.generate(assembled, FSharpSettings.fromNode(settings), manifest, List.of());
    return manifest;
  }
}

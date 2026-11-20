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
                + "global.Example.Cafe.OrderOutput, global.Example.Cafe.OrderError>>"),
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
  void anOperationThatDeclaresErrorsReturnsItsUnion() {
    MockManifest manifest = generate(MODEL, true);
    String operations = manifest.expectFileString(OPERATIONS);
    String client = manifest.expectFileString(CLIENT);

    assertTrue(
        operations.contains(
            "[<RequireQualifiedAccess>]\ntype OrderError =\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut\n"),
        operations);
    // The union holds the record; the runtime sees the exception that carries it.
    assertTrue(
        operations.contains(
            "| OrderError.SoldOut error -> global.Example.Cafe.Exceptions.SoldOut(error) :>"
                + " global.System.Exception"),
        operations);
    assertTrue(
        operations.contains(
            "| :? global.Example.Cafe.Exceptions.SoldOut as error ->"
                + " global.Microsoft.FSharp.Core.Option.Some(OrderError.SoldOut error.Error)"),
        operations);
    assertTrue(
        client.contains(
            "| error when (global.Example.Cafe.OrderError.FromException error).IsSome -> return"
                + " global.Microsoft.FSharp.Core.Result.Error((global.Example.Cafe.OrderError"
                + ".FromException error).Value)"),
        client);
    assertFalse(client.contains("type OrderError"), client);
    assertFalse(operations.contains("type PingError"), operations);
  }

  private static final String SERVICE_ERRORS =
      MODEL.replace(
              "operations: [Order, Ping, Forget]",
              "operations: [Order, Ping, Forget]\n    errors: [Busy]")
          + "\n@error(\"server\")\n@httpError(503)\nstructure Busy {}\n";

  @Test
  void aServiceErrorIsACaseOfEveryOperationsUnion() {
    MockManifest manifest = generate(SERVICE_ERRORS, true);
    String operations = manifest.expectFileString(OPERATIONS);
    String client = manifest.expectFileString(CLIENT);

    assertTrue(
        operations.contains(
            "type OrderError =\n    | Busy of global.Example.Cafe.Busy\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut\n"),
        operations);
    assertTrue(
        operations.contains("type PingError =\n    | Busy of global.Example.Cafe.Busy\n"),
        operations);
    assertTrue(
        client.contains("global.Example.Cafe.OrderOutput, global.Example.Cafe.OrderError>>"),
        client);
    assertTrue(
        client.contains("global.Example.Cafe.PingOutput, global.Example.Cafe.PingError>>"), client);
    assertFalse(client.contains("CafeError"), client);
    // The service schema lists the service's errors once; the protocols add them to every
    // operation (ServiceSchema.ErrorsOf), so operation schemas list only their own.
    String schemas = manifest.expectFileString(SCHEMAS);
    assertEquals(
        1,
        schemas.split("OperationError<global.Example.Cafe.Exceptions.Busy", -1).length - 1,
        schemas);
    assertTrue(schemas.contains("errors ="), schemas);
  }

  @Test
  void anErrorOnBothTheOperationAndTheServiceIsOneCase() {
    String model = SERVICE_ERRORS.replace("errors: [SoldOut]", "errors: [SoldOut, Busy]");

    String operations = generate(model, true).expectFileString(OPERATIONS);

    assertTrue(
        operations.contains(
            "type OrderError =\n    | Busy of global.Example.Cafe.Busy\n"
                + "    | SoldOut of global.Example.Cafe.SoldOut\n\n"),
        operations);
  }

  @Test
  void aShapeNamedLikeAnErrorTypeFails() {
    String model = referencing(MODEL, "OrderError");

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#OrderError"), error.getMessage());
  }

  @Test
  void aShapeTheRunDoesNotGenerateIsNoCollision() {
    // Neither shape is in the service's closure, so neither declares a type, and resolving their
    // names (Schemas is reserved) must not fail either.
    String model = MODEL + "\nstructure OrderError {}\nstructure Schemas {}\n";

    MockManifest manifest = generate(model, true);

    assertTrue(manifest.expectFileString(OPERATIONS).contains("type OrderError ="));
  }

  @Test
  void aShapeWithoutATypeOfItsOwnMayShareAnErrorTypesName() {
    String model = MODEL + "\nstring OrderError\nlist CafeClient { member: String }\n";

    MockManifest manifest = generate(model, true);

    assertTrue(manifest.expectFileString(OPERATIONS).contains("type OrderError ="));
    assertTrue(
        manifest.expectFileString(CLIENT).contains("type CafeClient private"),
        manifest.expectFileString(CLIENT));
  }

  @Test
  void anErrorNamedFromExceptionFails() {
    String model =
        MODEL.replace("errors: [SoldOut]", "errors: [SoldOut, FromException]")
            + "\n@error(\"client\")\nstructure FromException {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#FromException"), error.getMessage());
  }

  @Test
  void anErrorNamedToExceptionFails() {
    String model =
        MODEL.replace("errors: [SoldOut]", "errors: [SoldOut, ToException]")
            + "\n@error(\"client\")\nstructure ToException {}\n";

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#ToException"), error.getMessage());
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
    String model = referencing(MODEL, "CafeClient");

    CodegenException error = assertThrows(CodegenException.class, () -> generate(model, true));
    assertTrue(error.getMessage().contains("example.cafe#CafeClient"), error.getMessage());
  }

  /** The model with structure {@code name}, which the Order operation's output targets. */
  private static String referencing(String model, String name) {
    return model.replace(
            "        id: String\n", "        id: String\n        value: " + name + "\n")
        + "\nstructure "
        + name
        + " {}\n";
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

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

class ClientGeneratorTest {

  private static final Path CLIENT = Path.of("Client.g.fs");
  private static final Path SCHEMAS = Path.of("Schemas.g.fs");

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
                + " global.System.Threading.Tasks.Task<global.Example.Cafe.OrderOutput>"),
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
  }

  @Test
  void fillsInAMissingIdempotencyToken() {
    String client = generate(MODEL, true).expectFileString(CLIENT);

    assertTrue(client.contains("config.IdempotencyTokenProvider"), client);
    assertTrue(client.contains("Token = (match input.Token with"), client);
  }

  @Test
  void anOperationSchemaListsItsErrorsWithTheirStatusCodes() {
    String schemas = generate(MODEL, true).expectFileString(SCHEMAS);

    assertTrue(schemas.contains("type OrderSchema ="), schemas);
    assertTrue(
        schemas.contains("global.NSmithy.Core.ShapeId.Parse(\"example.cafe#SoldOut\")"), schemas);
    assertTrue(
        schemas.contains(", 409) :> global.NSmithy.Core.Serde.IOperationErrorSchema"), schemas);
    assertTrue(schemas.contains("type CafeSchema ="), schemas);
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

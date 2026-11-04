package io.github.thomaslaich.nsmithy.fsharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

/** An F# project generates the shapes in its own model files; see designs/shape-ownership.md. */
class FSharpShapeOwnershipTest {

  private static final Path TYPES = Path.of("Types.g.fs");
  private static final Path OPERATIONS = Path.of("Operations.g.fs");
  private static final Path SERVER = Path.of("Server.g.fs");

  private static final String PROTOCOLS =
      """
      $version: "2"
      namespace aws.protocols

      @trait(selector: "service")
      structure restJson1 {}
      """;

  /** A module's own model: an operation that targets Money from the common model. */
  private static final String ORDERS =
      """
      $version: "2"
      namespace acme.shop

      /// Places an order.
      @http(method: "POST", uri: "/orders")
      operation PlaceOrder {
          input := { total: Money }
          output := { orderId: String }
      }
      """;

  /** The common model: Money is targeted by the module, Unused by nobody. */
  private static final String COMMON =
      """
      $version: "2"
      namespace acme.shop

      structure Money {
          amount: Integer
      }

      structure Unused {
          value: String
      }
      """;

  /** The service that ties the module's operation into an API. */
  private static final String API =
      """
      $version: "2"
      namespace acme.shop

      use aws.protocols#restJson1

      @restJson1
      service Shop {
          operations: [PlaceOrder]
      }
      """;

  @TempDir Path directory;

  @Test
  void aProjectWithoutAServiceGeneratesItsOperationsAndWhatTheyTarget() {
    MockManifest manifest = generate(owned("orders.smithy"));

    String types = manifest.expectFileString(TYPES);
    assertTrue(types.contains("type PlaceOrderInput ="), types);
    assertTrue(types.contains("type Money ="), types);
    assertFalse(types.contains("type Unused ="), types);
    String operations = manifest.expectFileString(OPERATIONS);
    assertTrue(operations.contains("type PlaceOrderSchema ="), operations);
    assertTrue(operations.contains("type IPlaceOrderHandler ="), operations);
    assertTrue(operations.contains("/// Places an order."), operations);
    assertFalse(manifest.hasFile(SERVER));
  }

  @Test
  void anOwnedOperationGetsItsHandlerWithoutTheServer() {
    MockManifest manifest = generate(owned("orders.smithy").withMember("generateServer", false));

    assertTrue(manifest.expectFileString(OPERATIONS).contains("type IPlaceOrderHandler ="));
  }

  @Test
  void theManifestListsWhatTheRunGenerated() {
    ObjectNode manifest = manifestNode(generate(owned("orders.smithy")));

    assertEquals(
        ShapeManifest.FSHARP_GENERATOR, manifest.expectStringMember("generator").getValue());
    ObjectNode shapes = manifest.expectObjectMember("shapes");
    ObjectNode operation = shapes.expectObjectMember("acme.shop#PlaceOrder");
    assertEquals(
        "Acme.Shop.Schemas.PlaceOrderSchema", operation.expectStringMember("schema").getValue());
    assertEquals(
        "Acme.Shop.IPlaceOrderHandler", operation.expectStringMember("handler").getValue());
    ObjectNode money = shapes.expectObjectMember("acme.shop#Money");
    assertEquals("Acme.Shop.Money", money.expectStringMember("type").getValue());
    assertEquals("Acme.Shop.Schemas.MoneySchema", money.expectStringMember("schema").getValue());
    assertFalse(shapes.containsMember("acme.shop#Unused"));
  }

  @Test
  void aServiceUsesTheShapesItsReferencesGenerated() throws IOException {
    Path orders = writeManifest("orders.json", generate(owned("orders.smithy")));

    MockManifest manifest =
        generate(
            owned("api.smithy")
                .withMember("service", "acme.shop#Shop")
                .withMember("providedShapes", ArrayNode.fromStrings(List.of(orders.toString()))));

    // The module's types, schemas, and handler come from its assembly.
    assertFalse(manifest.hasFile(TYPES));
    assertFalse(manifest.hasFile(OPERATIONS));
    String server = manifest.expectFileString(SERVER);
    assertTrue(
        server.contains(
            "type IShopServiceHandler =\n    inherit global.Acme.Shop.IPlaceOrderHandler"),
        server);
    assertTrue(server.contains("global.Acme.Shop.Schemas.PlaceOrderSchema.Schema"), server);
    ObjectNode shapes = manifestNode(manifest).expectObjectMember("shapes");
    assertFalse(shapes.containsMember("acme.shop#PlaceOrder"));
    assertTrue(shapes.containsMember("acme.shop#Shop"));
  }

  @Test
  void aCSharpManifestListingAGeneratedShapeFails() throws IOException {
    Path csharp = writeCSharpManifest("acme.shop#Money");

    CodegenException error =
        assertThrows(
            CodegenException.class,
            () ->
                generate(
                    owned("orders.smithy")
                        .withMember(
                            "providedShapes", ArrayNode.fromStrings(List.of(csharp.toString())))));

    assertTrue(error.getMessage().contains("acme.shop#Money"), error.getMessage());
    assertTrue(error.getMessage().contains("SmithyGenerateCode"), error.getMessage());
  }

  @Test
  void aCSharpManifestOfOtherShapesIsIgnored() throws IOException {
    Path csharp = writeCSharpManifest("acme.other#Elsewhere");

    MockManifest manifest =
        generate(
            owned("orders.smithy")
                .withMember("providedShapes", ArrayNode.fromStrings(List.of(csharp.toString()))));

    assertTrue(manifest.expectFileString(TYPES).contains("type Money ="));
  }

  private Path writeManifest(String name, MockManifest generated) throws IOException {
    Path file = directory.resolve(name);
    Files.writeString(file, generated.expectFileString(Path.of(ShapeManifest.FILE_NAME)));
    return file;
  }

  private Path writeCSharpManifest(String shape) throws IOException {
    Path file = directory.resolve("csharp.json");
    Files.writeString(
        file,
        Node.prettyPrintJson(
            Node.objectNodeBuilder()
                .withMember("version", ShapeManifest.FORMAT_VERSION)
                .withMember("generator", ShapeManifest.CSHARP_GENERATOR)
                .withMember("generatorVersion", "0.0.0-other")
                .withMember(
                    "shapes",
                    Node.objectNode()
                        .withMember(shape, Node.objectNode().withMember("type", "Some.Type")))
                .build()));
    return file;
  }

  private static ObjectNode.Builder owned(String source) {
    return ObjectNode.builder().withMember("ownedSources", ArrayNode.fromStrings(List.of(source)));
  }

  private static MockManifest generate(ObjectNode.Builder settings) {
    Model model =
        Model.assembler()
            .addUnparsedModel("protocols.smithy", PROTOCOLS)
            .addUnparsedModel("orders.smithy", ORDERS)
            .addUnparsedModel("common.smithy", COMMON)
            .addUnparsedModel("api.smithy", API)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    FSharpCodegenPlugin.generate(
        model, FSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }

  private static ObjectNode manifestNode(MockManifest manifest) {
    return Node.parse(manifest.expectFileString(Path.of(ShapeManifest.FILE_NAME)))
        .expectObjectNode();
  }
}

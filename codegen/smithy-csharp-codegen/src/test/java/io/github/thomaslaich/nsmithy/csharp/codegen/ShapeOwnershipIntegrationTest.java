package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.ExpectationNotMetException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

/** A project generates the shapes in its own model files; see designs/shape-ownership.md. */
class ShapeOwnershipIntegrationTest {

  /** The module's own model: an operation and a service shape it does not generate. */
  private static final String ORDERS =
      """
      $version: "2"
      namespace acme.shop

      service Shop {
          operations: [PlaceOrder]
      }

      /// Places an order.
      operation PlaceOrder {
          input := { total: Money }
          output := { orderId: String }
      }
      """;

  /** Another project's model: Money is targeted by the module, Unused by nobody. */
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

  @Test
  void aProjectWithoutAServiceGeneratesItsOperationsAndWhatTheyTarget() {
    MockManifest manifest = generate(owned("orders.smithy"));

    assertTrue(manifest.hasFile(Path.of("Acme/Shop/PlaceOrderInput.g.cs")));
    assertTrue(manifest.hasFile(Path.of("Acme/Shop/PlaceOrderOutput.g.cs")));
    assertTrue(manifest.hasFile(Path.of("Acme/Shop/PlaceOrder.Schema.g.cs")));
    String handler = manifest.expectFileString(Path.of("Acme/Shop/PlaceOrder.Handler.Server.g.cs"));
    assertTrue(handler.contains("public interface IPlaceOrderHandler"), handler);
    assertTrue(handler.contains("/// Places an order."), handler);
    // Money is targeted but owned by no project in this build, so it is generated where needed.
    assertTrue(manifest.hasFile(Path.of("Acme/Shop/Money.g.cs")));
    assertFalse(manifest.hasFile(Path.of("Acme/Shop/Unused.g.cs")));
  }

  @Test
  void aServiceInTheOwnedModelGeneratesNothingWithoutSmithyService() {
    MockManifest manifest = generate(owned("orders.smithy"));

    assertFalse(manifest.hasFile(Path.of("Acme/Shop/Shop.Schema.g.cs")));
    assertFalse(manifest.hasFile(Path.of("Acme/Shop/Shop.Client.g.cs")));
    assertFalse(manifest.hasFile(Path.of("Acme/Shop/Shop.Server.g.cs")));
  }

  @Test
  void theManifestListsWhatTheRunGenerated() {
    ObjectNode shapes = shapes(generate(owned("orders.smithy")));

    assertEquals(
        "Acme.Shop.Schemas.PlaceOrderSchema",
        shapes.expectObjectMember("acme.shop#PlaceOrder").expectStringMember("schema").getValue());
    assertEquals(
        "Acme.Shop.IPlaceOrderHandler",
        shapes.expectObjectMember("acme.shop#PlaceOrder").expectStringMember("handler").getValue());
    ObjectNode input = shapes.expectObjectMember("acme.shop#PlaceOrderInput");
    assertEquals("Acme.Shop.PlaceOrderInput", input.expectStringMember("type").getValue());
    assertEquals(
        "Acme.Shop.Schemas.PlaceOrderInputSchema", input.expectStringMember("schema").getValue());
    assertTrue(shapes.getMember("acme.shop#Money").isPresent(), shapes.toString());
    assertFalse(shapes.getMember("acme.shop#Shop").isPresent(), shapes.toString());
    assertFalse(shapes.getMember("acme.shop#Unused").isPresent(), shapes.toString());
  }

  @Test
  void theManifestNamesItsFormatAndGenerator() {
    ObjectNode node = manifestNode(generate(owned("orders.smithy")));

    assertEquals(1, node.expectNumberMember("version").getValue().intValue());
    assertEquals("smithy-csharp-codegen", node.expectStringMember("generator").getValue());
    assertFalse(node.expectStringMember("generatorVersion").getValue().isBlank());
  }

  @Test
  void aServiceProjectGeneratesItsServiceAndOwnedShapesTheServiceDoesNotReach() {
    MockManifest manifest =
        generate(
            settings()
                .withMember("service", "acme.shop#Shop")
                .withMember("ownedSources", ArrayNode.fromStrings(List.of("common.smithy"))));

    assertTrue(manifest.hasFile(Path.of("Acme/Shop/Shop.Server.g.cs")));
    assertTrue(manifest.hasFile(Path.of("Acme/Shop/PlaceOrder.Handler.Server.g.cs")));
    // Unused is reached by nothing, but the project owns it.
    assertTrue(manifest.hasFile(Path.of("Acme/Shop/Unused.g.cs")));
    ObjectNode shapes = shapes(manifest);
    assertEquals(
        "Acme.Shop.Schemas.ShopSchema",
        shapes.expectObjectMember("acme.shop#Shop").expectStringMember("schema").getValue());
  }

  @Test
  void aServiceProjectWithoutOwnedSourcesWritesAManifestToo() {
    ObjectNode shapes = shapes(generate(settings().withMember("service", "acme.shop#Shop")));

    assertTrue(shapes.getMember("acme.shop#PlaceOrder").isPresent(), shapes.toString());
    assertFalse(shapes.getMember("acme.shop#Unused").isPresent(), shapes.toString());
  }

  @Test
  void withoutTheServerOperationsGetNoHandler() {
    MockManifest manifest =
        generate(
            settings()
                .withMember("ownedSources", ArrayNode.fromStrings(List.of("orders.smithy")))
                .withMember("generateServer", false));

    assertTrue(manifest.hasFile(Path.of("Acme/Shop/PlaceOrder.Schema.g.cs")));
    assertFalse(manifest.hasFile(Path.of("Acme/Shop/PlaceOrder.Handler.Server.g.cs")));
    assertFalse(
        shapes(manifest)
            .expectObjectMember("acme.shop#PlaceOrder")
            .getMember("handler")
            .isPresent());
  }

  @Test
  void aProjectThatOwnsNothingWritesAnEmptyManifest() {
    MockManifest manifest = generate(settings().withMember("ownedSources", ArrayNode.arrayNode()));

    assertTrue(shapes(manifest).isEmpty());
    assertEquals(1, manifest.getFiles().size(), manifest.getFiles().toString());
  }

  @Test
  void anOperationOutsideTheServiceNamespaceKeepsItsHandlerInItsOwnNamespace() {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "service.smithy",
                """
                $version: "2"
                namespace acme.api

                service Api {
                    operations: [acme.orders#PlaceOrder]
                }
                """)
            .addUnparsedModel(
                "orders.smithy",
                """
                $version: "2"
                namespace acme.orders

                operation PlaceOrder {
                    input := { id: String }
                }
                """)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(
        model,
        CSharpSettings.fromNode(settings().withMember("service", "acme.api#Api").build()),
        manifest,
        List.of());

    String handler =
        manifest.expectFileString(Path.of("Acme/Orders/PlaceOrder.Handler.Server.g.cs"));
    assertTrue(handler.contains("namespace Acme.Orders;"), handler);
    String server = manifest.expectFileString(Path.of("Acme/Api/Api.Server.g.cs"));
    assertTrue(
        server.contains(
            "public interface IApiServiceHandler : global::Acme.Orders.IPlaceOrderHandler"),
        server);
  }

  @Test
  void settingsNeedAServiceAClosureOrOwnedSources() {
    ExpectationNotMetException exception =
        assertThrows(
            ExpectationNotMetException.class, () -> CSharpSettings.fromNode(Node.objectNode()));

    assertTrue(exception.getMessage().contains("ownedSources"), exception.getMessage());
  }

  private static ObjectNode.Builder settings() {
    return ObjectNode.builder().withMember("baseNamespace", "");
  }

  private static ObjectNode.Builder owned(String source) {
    return settings().withMember("ownedSources", ArrayNode.fromStrings(List.of(source)));
  }

  private static MockManifest generate(ObjectNode.Builder settings) {
    Model model =
        Model.assembler()
            .addUnparsedModel("orders.smithy", ORDERS)
            .addUnparsedModel("common.smithy", COMMON)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(
        model, CSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }

  private static ObjectNode manifestNode(MockManifest manifest) {
    return Node.parse(manifest.expectFileString(Path.of(ShapeManifest.FILE_NAME)))
        .expectObjectNode();
  }

  private static ObjectNode shapes(MockManifest manifest) {
    return manifestNode(manifest).expectObjectMember("shapes");
  }
}

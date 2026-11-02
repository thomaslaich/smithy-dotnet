package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

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

/**
 * A project refers to the shapes its references generated instead of generating them again; see
 * designs/shape-ownership.md.
 */
class ProvidedShapesIntegrationTest {

  @TempDir Path tempDir;

  /** The Orders module's model: one operation, and the Money shape it targets. */
  private static final String ORDERS =
      """
      $version: "2"
      namespace acme.shop

      operation PlaceOrder {
          input := { total: Money }
          output := { orderId: String }
      }

      structure Money {
          amount: Integer
      }
      """;

  /** The Api project's model: the service that ties the module's operation in. */
  private static final String SERVICE =
      """
      $version: "2"
      namespace acme.shop

      service Shop {
          operations: [PlaceOrder]
      }
      """;

  @Test
  void aServiceProjectReferencesTheShapesItsModuleGenerated() throws Exception {
    MockManifest api = generateApi(ordersManifest(true));

    assertFalse(api.hasFile(Path.of("Acme/Shop/PlaceOrderInput.g.cs")));
    assertFalse(api.hasFile(Path.of("Acme/Shop/PlaceOrder.Schema.g.cs")));
    assertFalse(api.hasFile(Path.of("Acme/Shop/PlaceOrder.Handler.Server.g.cs")));
    assertFalse(api.hasFile(Path.of("Acme/Shop/Money.g.cs")));
    assertTrue(api.hasFile(Path.of("Acme/Shop/Shop.Schema.g.cs")));
  }

  @Test
  void theServerUsesTheModuleNamesFromTheManifest() throws Exception {
    String server = generateApi(ordersManifest(true)).expectFileString(Path.of("Acme/Shop/Shop.Server.g.cs"));

    // The handler interface comes from the module's namespace, which the file imports.
    assertTrue(server.contains("using Orders.Acme.Shop;"), server);
    assertTrue(server.contains("public interface IShopServiceHandler : IPlaceOrderHandler"), server);
    assertTrue(server.contains("GetRequiredService<IPlaceOrderHandler>()"), server);
    assertTrue(server.contains("global::Orders.Acme.Shop.Schemas.PlaceOrderSchema.Schema"), server);
    assertFalse(server.contains("global::Acme.Shop.Schemas.PlaceOrderSchema"), server);
  }

  @Test
  void theClientUsesTheModuleTypes() throws Exception {
    String client = generateApi(ordersManifest(true)).expectFileString(Path.of("Acme/Shop/Shop.Client.g.cs"));

    assertTrue(client.contains("global::Orders.Acme.Shop.PlaceOrderInput"), client);
    assertTrue(client.contains("global::Orders.Acme.Shop.PlaceOrderOutput"), client);
    assertFalse(client.contains("global::Acme.Shop.PlaceOrder"), client);
  }

  @Test
  void theServiceProjectManifestListsOnlyWhatItGenerated() throws Exception {
    ObjectNode shapes =
        Node.parse(
                generateApi(ordersManifest(true))
                    .expectFileString(Path.of(ShapeManifest.FILE_NAME)))
            .expectObjectNode()
            .expectObjectMember("shapes");

    assertTrue(shapes.getMember("acme.shop#Shop").isPresent(), shapes.toString());
    assertFalse(shapes.getMember("acme.shop#PlaceOrder").isPresent(), shapes.toString());
    assertFalse(shapes.getMember("acme.shop#Money").isPresent(), shapes.toString());
  }

  @Test
  void aShapeProvidedTwiceFails() throws Exception {
    Path first = ordersManifest(true);
    Path second = Files.copy(first, tempDir.resolve("second.json"));

    CodegenException exception =
        assertThrows(CodegenException.class, () -> generateApi(first, second));

    assertTrue(exception.getMessage().contains(first.toString()), exception.getMessage());
    assertTrue(exception.getMessage().contains(second.toString()), exception.getMessage());
  }

  @Test
  void aManifestFromAnotherGeneratorVersionFails() throws Exception {
    Path manifest = ordersManifest(true);
    Files.writeString(
        manifest,
        Files.readString(manifest)
            .replace(
                "\"generatorVersion\": \"" + ShapeManifest.generatorVersion() + "\"",
                "\"generatorVersion\": \"0.0.1\""));

    CodegenException exception = assertThrows(CodegenException.class, () -> generateApi(manifest));

    assertTrue(exception.getMessage().contains("same NSmithy version"), exception.getMessage());
  }

  @Test
  void aProjectGivenItsOwnManifestFails() throws Exception {
    Path manifest = ordersManifest(true);

    CodegenException exception =
        assertThrows(
            CodegenException.class,
            () ->
                generate(
                    model(),
                    settings("Orders")
                        .withMember("ownedSources", ArrayNode.fromStrings(List.of("orders.smithy")))
                        .withMember(
                            "providedShapes", ArrayNode.fromStrings(List.of(manifest.toString())))));

    assertTrue(exception.getMessage().contains("own model files"), exception.getMessage());
  }

  @Test
  void aServiceRenameOfAProvidedShapeFails() throws Exception {
    Path manifest = ordersManifest(true);
    Model model =
        Model.assembler()
            .addUnparsedModel("orders.smithy", ORDERS)
            .addUnparsedModel(
                "service.smithy",
                """
                $version: "2"
                namespace acme.shop

                service Shop {
                    operations: [PlaceOrder]
                    rename: { "acme.shop#Money": "Cash" }
                }
                """)
            .assemble()
            .unwrap();

    CodegenException exception =
        assertThrows(
            CodegenException.class,
            () ->
                generate(
                    model,
                    settings("")
                        .withMember("service", "acme.shop#Shop")
                        .withMember(
                            "providedShapes", ArrayNode.fromStrings(List.of(manifest.toString())))));

    assertTrue(exception.getMessage().contains("renames acme.shop#Money"), exception.getMessage());
  }

  @Test
  void aProvidedOperationWithoutAHandlerFailsForAServer() throws Exception {
    CodegenException exception =
        assertThrows(CodegenException.class, () -> generateApi(ordersManifest(false)));

    assertTrue(exception.getMessage().contains("without a handler interface"), exception.getMessage());
  }

  @Test
  void aClientNeedsNoHandlers() throws Exception {
    Path manifest = ordersManifest(false);
    MockManifest client =
        generate(
            model(),
            settings("")
                .withMember("service", "acme.shop#Shop")
                .withMember("generateServer", false)
                .withMember("providedShapes", ArrayNode.fromStrings(List.of(manifest.toString()))));

    assertTrue(client.hasFile(Path.of("Acme/Shop/Shop.Client.g.cs")));
  }

  /** Generates the Orders module, with the base namespace "Orders", and saves its manifest. */
  private Path ordersManifest(boolean generateServer) throws Exception {
    MockManifest orders =
        generate(
            model(),
            settings("Orders")
                .withMember("ownedSources", ArrayNode.fromStrings(List.of("orders.smithy")))
                .withMember("generateServer", generateServer));
    Path file = Files.createTempFile(tempDir, "orders-", ".json");
    Files.writeString(file, orders.expectFileString(Path.of(ShapeManifest.FILE_NAME)));
    return file;
  }

  private static MockManifest generateApi(Path... manifests) {
    return generate(
        model(),
        settings("")
            .withMember("service", "acme.shop#Shop")
            .withMember(
                "providedShapes",
                ArrayNode.fromStrings(
                    java.util.Arrays.stream(manifests).map(Path::toString).toList())));
  }

  private static Model model() {
    return Model.assembler()
        .addUnparsedModel("orders.smithy", ORDERS)
        .addUnparsedModel("service.smithy", SERVICE)
        .assemble()
        .unwrap();
  }

  private static ObjectNode.Builder settings(String baseNamespace) {
    return ObjectNode.builder().withMember("baseNamespace", baseNamespace);
  }

  private static MockManifest generate(Model model, ObjectNode.Builder settings) {
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(
        model, CSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }
}

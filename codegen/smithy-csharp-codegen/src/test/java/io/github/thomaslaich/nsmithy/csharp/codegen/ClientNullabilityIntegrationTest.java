package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

/**
 * Smithy's client rules for member optionality: a project that generates only a client makes every
 * {@code @input} member and every {@code @clientOptional} member nullable.
 */
class ClientNullabilityIntegrationTest {

  private static final String MODEL =
      """
      $version: "2"
      namespace example.shop

      use smithy.framework#ValidationException

      service Shop {
          version: "2024-01-01"
          operations: [PlaceOrder]
      }

      operation PlaceOrder {
          input := {
              @required
              item: String
          }
          output := {
              @required
              @clientOptional
              orderId: String

              @required
              total: Integer
          }
          errors: [ValidationException]
      }
      """;

  @Test
  void aClientOnlyProjectFollowsTheClientRules() {
    MockManifest manifest = generate(settings().withMember("generateServer", false));

    assertTrue(input(manifest).contains("PlaceOrderInput(string? Item = null)"), input(manifest));
    assertTrue(
        output(manifest).contains("PlaceOrderOutput(int Total, string? OrderId = null)"),
        output(manifest));
  }

  @Test
  void aProjectThatGeneratesAServerFollowsTheServerRules() {
    MockManifest manifest = generate(settings());

    assertTrue(input(manifest).contains("PlaceOrderInput(string Item)"), input(manifest));
    assertTrue(
        output(manifest).contains("PlaceOrderOutput(string OrderId, int Total)"), output(manifest));
  }

  @Test
  void aClientOnlyProjectThatOwnsTheOperationsKeepsTheServerRules() {
    MockManifest manifest =
        generate(
            settings()
                .withMember("generateServer", false)
                .withMember("ownedSources", ArrayNode.fromStrings(List.of("shop.smithy"))));

    assertTrue(input(manifest).contains("PlaceOrderInput(string Item)"), input(manifest));
  }

  private static String input(MockManifest manifest) {
    return file(manifest, "PlaceOrderInput");
  }

  private static String output(MockManifest manifest) {
    return file(manifest, "PlaceOrderOutput");
  }

  private static String file(MockManifest manifest, String type) {
    return manifest
        .expectFileString(Path.of("Example/Shop/" + type + ".g.cs"))
        .replaceAll("\\s+", " ");
  }

  private static ObjectNode.Builder settings() {
    return Node.objectNodeBuilder().withMember("service", "example.shop#Shop");
  }

  private static MockManifest generate(ObjectNode.Builder settings) {
    Model model =
        Model.assembler()
            .discoverModels() // the validation model's shapes
            .addUnparsedModel("shop.smithy", MODEL)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    CSharpCodegenPlugin.generate(
        model, CSharpSettings.fromNode(settings.build()), manifest, List.of());
    return manifest;
  }
}

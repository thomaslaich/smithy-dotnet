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
import software.amazon.smithy.model.node.ExpectationNotMetException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class FSharpCodegenPluginTest {

  private static final Path TYPES = Path.of("Types.g.fs");
  private static final Path SCHEMAS = Path.of("Schemas.g.fs");

  private static final String KITCHEN =
      """
      $version: "2"
      namespace a.kitchen

      use z.shared#Money

      service Kitchen {
          version: "2024-01-01"
          operations: [Order]
      }

      operation Order {
          input := {
              @required
              dish: Dish
              tip: Money
          }
          output := {
              receipt: Receipt
          }
          errors: [OutOfStock]
      }

      structure Dish {
          @required
          name: String
          @default(2)
          slices: Integer
          notes: Notes
      }

      @sparse
      list Notes {
          member: String
      }

      union Receipt {
          paid: Money
          comped: Unit
          unknown: String
      }

      enum Ingredient {
          BACON
          VALUE = "value"
      }

      @error("client")
      structure OutOfStock {
          message: String
          @required
          item: Ingredient
          data: String
      }
      """;

  private static final String SHARED =
      """
      $version: "2"
      namespace z.shared

      structure Money {
          @required
          amount: BigDecimal
      }
      """;

  @Test
  void generatesTypesAndSchemasForAService() {
    MockManifest manifest = generate(settings("a.kitchen#Kitchen", null), KITCHEN, SHARED);

    String types = manifest.expectFileString(TYPES);
    assertTrue(types.contains("namespace rec A.Kitchen"), types);
    assertTrue(
        types.contains("type Dish =\n    {\n        Name: string\n        Slices: int\n"), types);
    assertTrue(types.contains("Notes: string option list option"), types);
    assertTrue(types.contains("[<RequireQualifiedAccess>]\ntype Receipt ="), types);
    String schemas = manifest.expectFileString(SCHEMAS);
    assertTrue(schemas.contains("namespace rec A.Kitchen.Schemas"), schemas);
    assertTrue(schemas.contains("type DishSchema ="), schemas);
    assertTrue(schemas.contains("ValueNone -> 2)"), schemas);
  }

  @Test
  void aNamespaceComesAfterTheNamespacesItRefersTo() {
    MockManifest manifest = generate(settings("a.kitchen#Kitchen", null), KITCHEN, SHARED);

    for (Path file : List.of(TYPES, SCHEMAS)) {
      String code = manifest.expectFileString(file);
      int shared = code.indexOf("namespace rec Z.Shared");
      int kitchen = code.indexOf("namespace rec A.Kitchen");
      assertTrue(shared >= 0 && shared < kitchen, code);
    }
  }

  @Test
  void namespacesThatReferToEachOtherFail() {
    String first =
        """
        $version: "2"
        namespace example.first
        structure A { b: example.second#B }
        """;
    String second =
        """
        $version: "2"
        namespace example.second
        structure B { a: example.first#A }
        """;
    String closure =
        """
        $version: "2"
        metadata shapeClosures = [{ id: "example.first#all", includeNamespaces: ["example.first"] }]
        """;

    CodegenException error =
        assertThrows(
            CodegenException.class,
            () -> generate(settings(null, "example.first#all"), first, second, closure));
    assertTrue(error.getMessage().contains("example.first, example.second"), error.getMessage());
  }

  @Test
  void namesThatClashTakeASuffix() {
    MockManifest manifest = generate(settings("a.kitchen#Kitchen", null), KITCHEN, SHARED);

    String types = manifest.expectFileString(TYPES);
    assertTrue(types.contains("| UnknownCase of string"), types);
    assertTrue(
        types.contains("| Unknown of tag: string * value: global.NSmithy.Core.Document"), types);
    assertTrue(types.contains("| VALUE\n"), types);
    assertTrue(
        types.contains(
            "type OutOfStock(item: global.A.Kitchen.Ingredient, ?errorMessage: string, ?dataValue:"
                + " string) ="),
        types);
  }

  @Test
  void onlySparseCollectionsGetTheOptionAdapter() {
    MockManifest manifest = generate(settings("a.kitchen#Kitchen", null), KITCHEN, SHARED);

    String schemas = manifest.expectFileString(SCHEMAS);
    assertEquals(1, schemas.split("type internal SchemaOptionAdapter<'T>", -1).length - 1, schemas);
    assertTrue(
        schemas.indexOf("SchemaOptionAdapter<'T>") > schemas.indexOf("namespace rec A.Kitchen"),
        schemas);
  }

  @Test
  void aClosureAloneGeneratesItsTypes() {
    String model =
        """
        $version: "2"
        metadata shapeClosures = [{ id: "example.events#all", includeNamespaces: ["example.events"] }]
        namespace example.events
        structure Joined { name: String }
        """;

    MockManifest manifest = generate(settings(null, "example.events#all"), model);

    assertTrue(manifest.expectFileString(TYPES).contains("type Joined ="));
  }

  @Test
  void requiresAServiceOrAClosure() {
    assertThrows(
        ExpectationNotMetException.class, () -> FSharpSettings.fromNode(Node.objectNode()));
  }

  @Test
  void generatesNothingWhenNoShapeNeedsCode() {
    String model =
        """
        $version: "2"
        namespace example.empty
        service Empty { version: "1" }
        """;

    MockManifest manifest = generate(settings("example.empty#Empty", null), model);

    assertFalse(manifest.hasFile(TYPES));
    assertFalse(manifest.hasFile(SCHEMAS));
  }

  private static ObjectNode settings(String service, String closure) {
    ObjectNode.Builder settings = Node.objectNodeBuilder();
    if (service != null) {
      settings.withMember("service", service);
    }
    if (closure != null) {
      settings.withMember("closure", closure);
    }
    return settings.build();
  }

  private static MockManifest generate(ObjectNode settings, String... models) {
    var assembler = Model.assembler();
    for (int i = 0; i < models.length; i++) {
      assembler.addUnparsedModel("model" + i + ".smithy", models[i]);
    }
    Model model = assembler.assemble().unwrap();
    MockManifest manifest = new MockManifest();
    FSharpCodegenPlugin.generate(model, FSharpSettings.fromNode(settings), manifest, List.of());
    return manifest;
  }
}

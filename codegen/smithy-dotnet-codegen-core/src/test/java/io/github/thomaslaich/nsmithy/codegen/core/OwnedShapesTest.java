package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Path;
import java.util.List;
import java.util.Set;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ShapeId;

final class OwnedShapesTest {

  private static final String ORDERS =
      """
      $version: "2"
      namespace acme.shop

      service Shop {
          operations: [PlaceOrder]
      }

      operation PlaceOrder {
          input := { total: Money }
      }

      @trait
      structure audited {}
      """;

  private static final String COMMON =
      """
      $version: "2"
      namespace acme.shop

      structure Money {
          amount: Integer
      }
      """;

  private static Model model() {
    return Model.assembler()
        .addUnparsedModel("module/orders.smithy", ORDERS)
        .addUnparsedModel("common/common.smithy", COMMON)
        .assemble()
        .unwrap();
  }

  @Test
  void ownsTheShapesDefinedInItsSources() {
    Set<ShapeId> owned = OwnedShapes.of(model(), List.of("module"));

    assertEquals(
        Set.of(ShapeId.from("acme.shop#PlaceOrder"), ShapeId.from("acme.shop#PlaceOrderInput")),
        owned);
  }

  @Test
  void leavesOutServicesTraitsMembersAndForeignShapes() {
    Set<ShapeId> owned = OwnedShapes.of(model(), List.of("module"));

    assertTrue(owned.stream().noneMatch(id -> id.getName().equals("Shop")), owned.toString());
    assertTrue(owned.stream().noneMatch(id -> id.getName().equals("audited")), owned.toString());
    assertTrue(owned.stream().noneMatch(id -> id.getMember().isPresent()), owned.toString());
    assertTrue(owned.stream().noneMatch(id -> id.getName().equals("Money")), owned.toString());
  }

  @Test
  void acceptsFilesAndAbsolutePaths() {
    String file = Path.of("common/common.smithy").toAbsolutePath().toString();

    assertEquals(Set.of(ShapeId.from("acme.shop#Money")), OwnedShapes.of(model(), List.of(file)));
  }

  @Test
  void ownsNothingWithoutSources() {
    assertTrue(OwnedShapes.of(model(), List.of()).isEmpty());
  }
}

package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Set;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class ClientNullabilityTest {

  static final String MODEL =
      """
      $version: "2"
      namespace example.shop

      operation PlaceOrder {
          input := {
              @required
              item: String

              @required
              @default(1)
              quantity: Integer

              note: String
          }
          output := {
              @required
              @clientOptional
              orderId: String

              @required
              total: Integer

              @clientOptional
              @default(0)
              discount: Integer
          }
      }

      structure Shared {
          @required
          @clientOptional
          id: String
      }
      """;

  static final Model SHOP =
      Model.assembler().addUnparsedModel("shop.smithy", MODEL).assemble().unwrap();

  @Test
  void noInputMemberIsRequired() {
    Model model = ClientNullability.apply(SHOP, id -> true);

    assertTrue(ShapeSupport.isNullable(member(model, "PlaceOrderInput$item")));
    assertTrue(ShapeSupport.isNullable(member(model, "PlaceOrderInput$note")));
  }

  @Test
  void anInputMemberKeepsItsDefault() {
    Model model = ClientNullability.apply(SHOP, id -> true);

    MemberShape quantity = member(model, "PlaceOrderInput$quantity");
    assertFalse(ShapeSupport.isRequired(quantity));
    assertTrue(ShapeSupport.isOptionalParameter(quantity));
    assertTrue(ShapeSupport.hasDefault(quantity));
  }

  @Test
  void aClientOptionalMemberIsNullableAndOthersKeepTheirRules() {
    Model model = ClientNullability.apply(SHOP, id -> true);

    assertTrue(ShapeSupport.isNullable(member(model, "PlaceOrderOutput$orderId")));
    assertTrue(ShapeSupport.isNullable(member(model, "PlaceOrderOutput$discount")));
    assertFalse(ShapeSupport.isNullable(member(model, "PlaceOrderOutput$total")));
  }

  @Test
  void aShapeTheProjectDoesNotGenerateKeepsItsTypes() {
    ShapeId shared = ShapeId.from("example.shop#Shared");
    Model model = ClientNullability.apply(SHOP, id -> !id.equals(shared));

    assertFalse(ShapeSupport.isNullable(member(model, "Shared$id")));
    assertTrue(ShapeSupport.isNullable(member(model, "PlaceOrderOutput$orderId")));
  }

  @Test
  void onlyAClientOnlyProjectThatOwnsNoOperationFollowsTheClientRules() {
    ShapeId operation = ShapeId.from("example.shop#PlaceOrder");
    ShapeId shared = ShapeId.from("example.shop#Shared");

    assertTrue(ClientNullability.applies(SHOP, Set.of(), true, false));
    assertTrue(ClientNullability.applies(SHOP, Set.of(shared), true, false));
    assertFalse(ClientNullability.applies(SHOP, Set.of(operation), true, false));
    assertFalse(ClientNullability.applies(SHOP, Set.of(), true, true));
    assertFalse(ClientNullability.applies(SHOP, Set.of(), false, true));
    assertFalse(ClientNullability.applies(SHOP, Set.of(), false, false));
  }

  private static MemberShape member(Model model, String id) {
    return model.expectShape(ShapeId.from("example.shop#" + id), MemberShape.class);
  }
}

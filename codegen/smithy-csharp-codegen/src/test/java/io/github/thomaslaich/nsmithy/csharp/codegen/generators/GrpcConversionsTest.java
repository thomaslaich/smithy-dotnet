package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;

final class GrpcConversionsTest {

  private static final String PROTO_TRAITS =
      """
      $version: "2"

      namespace alloy.proto

      use smithy.api#trait

      @trait(selector: "union")
      structure protoInlinedOneOf {}
      """;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.oneof

      use alloy.proto#protoInlinedOneOf

      service ExampleService {
          version: "1"
          operations: [Choose]
      }

      operation Choose {
          input: ChoiceInput
          output: ChoiceInput
      }

      structure ChoiceInput {
          selector: Choice
      }

      @protoInlinedOneOf
      union Choice {
          byName: String
      }
      """;

  @Test
  void grpcToSmithyUsesInlinedOneofMemberNameForCaseType() {
    ConversionFixture fixture = fixture();

    String expression =
        GrpcConversions.grpcToSmithy(
            fixture.symbolProvider(),
            fixture.model(),
            fixture.choiceInput(),
            "request",
            "My.Base.Example.Oneof.Grpc");

    assertTrue(
        expression.contains(
            "request.SelectorCase switch {"
                + " global::My.Base.Example.Oneof.Grpc.ChoiceInput.SelectorOneofCase.ByName"),
        expression);
    assertFalse(expression.contains(".FilterOneofCase."), expression);
  }

  @Test
  void grpcToSmithyUsesConfiguredGrpcNamespaceForInlinedOneofCaseType() {
    ConversionFixture fixture = fixture();

    String expression =
        GrpcConversions.grpcToSmithy(
            fixture.symbolProvider(),
            fixture.model(),
            fixture.choiceInput(),
            "request",
            "My.Base.Example.Oneof.Grpc");

    assertTrue(expression.contains("global::My.Base.Example.Oneof.Grpc.ChoiceInput"), expression);
    assertFalse(expression.contains("global::Example.Oneof.Grpc.ChoiceInput"), expression);
  }

  private static ConversionFixture fixture() {
    Model model =
        Model.assembler()
            .addUnparsedModel("proto-traits.smithy", PROTO_TRAITS)
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.oneof#ExampleService"))
                .withMember("baseNamespace", Node.from("My.Base"))
                .build());
    return new ConversionFixture(
        model,
        new CSharpSymbolProvider(model, settings),
        model.expectShape(ShapeId.from("example.oneof#ChoiceInput"), StructureShape.class));
  }

  private record ConversionFixture(
      Model model, CSharpSymbolProvider symbolProvider, StructureShape choiceInput) {}
}

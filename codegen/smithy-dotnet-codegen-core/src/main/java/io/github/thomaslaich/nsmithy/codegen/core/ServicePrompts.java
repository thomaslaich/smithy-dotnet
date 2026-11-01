package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/** The {@code smithy.ai#prompts} a service and its operations declare. */
@SmithyInternalApi
public final class ServicePrompts {

  /** A prompt template and the arguments it takes. */
  public record Prompt(
      String name,
      String description,
      String template,
      String preferWhen,
      List<Argument> arguments) {}

  /** A prompt argument: a member of the structure the prompt's {@code arguments} names. */
  public record Argument(String name, String description, boolean required) {}

  private ServicePrompts() {}

  /** The service's prompts, then each operation's, each set ordered by name. */
  public static List<Prompt> of(
      Model model, ServiceShape service, List<OperationShape> operations) {
    List<Prompt> prompts = new ArrayList<>();
    add(model, service, prompts);
    for (OperationShape operation : operations) {
      add(model, operation, prompts);
    }
    return List.copyOf(prompts);
  }

  private static void add(Model model, Shape owner, List<Prompt> prompts) {
    owner
        .findTrait(TraitIds.PROMPTS)
        .ifPresent(
            trait -> {
              var entries =
                  trait.toNode().expectObjectNode().getStringMap().entrySet().stream()
                      .sorted(Map.Entry.comparingByKey())
                      .toList();
              for (var entry : entries) {
                ObjectNode definition = entry.getValue().expectObjectNode();
                prompts.add(
                    new Prompt(
                        entry.getKey(),
                        definition.expectStringMember("description").getValue(),
                        definition.expectStringMember("template").getValue(),
                        definition
                            .getStringMember("preferWhen")
                            .map(StringNode::getValue)
                            .orElse(null),
                        definition
                            .getStringMember("arguments")
                            .map(node -> arguments(model, node.getValue()))
                            .orElse(List.of())));
              }
            });
  }

  private static List<Argument> arguments(Model model, String shapeId) {
    StructureShape structure = model.expectShape(ShapeId.from(shapeId), StructureShape.class);
    return structure.getAllMembers().values().stream()
        .sorted(Comparator.comparing(MemberShape::getMemberName))
        .map(
            member ->
                new Argument(
                    member.getMemberName(),
                    member
                        .getTrait(DocumentationTrait.class)
                        .map(DocumentationTrait::getValue)
                        .orElse(null),
                    member.hasTrait(RequiredTrait.class)))
        .toList();
  }
}

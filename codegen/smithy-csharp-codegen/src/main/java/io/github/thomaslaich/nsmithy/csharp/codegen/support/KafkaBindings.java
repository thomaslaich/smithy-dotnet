/*
 * Reads the Kafka capability bindings of a @kafkaJson service from the model.
 *
 * Shared by KafkaGenerator (the SDK) and KafkaDependencyInjectionGenerator (the
 * hosting extensions): both need the same view of which operations are
 * @kafkaProduce / @kafkaConsume capabilities and which C# types their payloads
 * map to.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class KafkaBindings {

  /** A @kafkaProduce operation: a command payload written to a topic. */
  public record Produce(
      String opName, // C# PascalCase operation name
      String topic,
      StructureShape command, // the (dedicated) command input structure
      String commandType // qualified C# type
      ) {}

  /** A @kafkaConsume operation: a @streaming union of @event payloads on a topic. */
  public record Consume(
      String topic,
      UnionShape union,
      String unionType, // qualified C# type
      List<MemberShape> members // sorted union members
      ) {}

  private KafkaBindings() {}

  /** The service's operations in stable (shape id) order. */
  public static List<OperationShape> operations(Model model, ServiceShape service) {
    return TopDownIndex.of(model).getContainedOperations(service).stream()
        .sorted(Comparator.comparing(op -> op.getId().toString()))
        .collect(Collectors.toList());
  }

  public static List<Produce> produces(Model model, SymbolProvider sp, ServiceShape service) {
    List<Produce> produces = new ArrayList<>();
    for (OperationShape op : operations(model, service)) {
      if (op.hasTrait(TraitIds.KAFKA_PRODUCE)) {
        produces.add(buildProduce(model, sp, op));
      }
    }
    return produces;
  }

  public static List<Consume> consumes(Model model, SymbolProvider sp, ServiceShape service) {
    List<Consume> consumes = new ArrayList<>();
    for (OperationShape op : operations(model, service)) {
      if (op.hasTrait(TraitIds.KAFKA_CONSUME)) {
        consumes.add(buildConsume(model, sp, op));
      }
    }
    return consumes;
  }

  private static Produce buildProduce(Model model, SymbolProvider sp, OperationShape op) {
    if (isUnit(op.getInputShape())) {
      throw new IllegalStateException(
          "@kafkaProduce operation " + op.getId() + " must have a @command input");
    }
    StructureShape command = model.expectShape(op.getInputShape(), StructureShape.class);
    String commandType = CSharpSymbolProvider.qualified(sp.toSymbol(command));
    return new Produce(
        CSharpNaming.typeName(op.getId().getName()),
        topicName(op, TraitIds.KAFKA_PRODUCE),
        command,
        commandType);
  }

  private static Consume buildConsume(Model model, SymbolProvider sp, OperationShape op) {
    StructureShape output = model.expectShape(op.getOutputShape(), StructureShape.class);
    MemberShape streamingMember =
        output.members().stream()
            .filter(m -> model.expectShape(m.getTarget()).hasTrait(TraitIds.STREAMING))
            .findFirst()
            .orElseThrow(
                () ->
                    new IllegalStateException(
                        "@kafkaConsume operation "
                            + op.getId()
                            + " output has no member targeting a @streaming union"));
    UnionShape union = model.expectShape(streamingMember.getTarget(), UnionShape.class);
    List<MemberShape> members =
        union.members().stream()
            .sorted(Comparator.comparing(MemberShape::getMemberName))
            .collect(Collectors.toList());
    String unionType = CSharpSymbolProvider.qualified(sp.toSymbol(union));
    return new Consume(topicName(op, TraitIds.KAFKA_CONSUME), union, unionType, members);
  }

  private static String topicName(OperationShape op, ShapeId capabilityTrait) {
    return op.findTrait(capabilityTrait)
        .map(t -> t.toNode().expectObjectNode())
        .flatMap(node -> node.getStringMember("topic"))
        .map(s -> s.getValue())
        .orElseThrow(
            () ->
                new IllegalStateException(
                    "@" + capabilityTrait.getName() + " missing topic on operation " + op.getId()));
  }

  private static final ShapeId UNIT = ShapeId.from("smithy.api#Unit");

  private static boolean isUnit(ShapeId id) {
    return UNIT.equals(id);
  }
}

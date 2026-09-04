/** Reads the Kafka capability bindings shared by SDK and hosting generators. */
package io.github.thomaslaich.nsmithy.bote.codegen.support;

import io.github.thomaslaich.nsmithy.bote.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
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

  public record Produce(String opName, String topic, StructureShape command, String commandType) {}

  public record Consume(
      String topic, UnionShape union, String unionType, List<MemberShape> members) {}

  public record TopicConfiguration(
      String topic,
      Integer partitions,
      Integer replicationFactor,
      Map<String, String> configuration) {}

  private KafkaBindings() {}

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

  public static List<TopicConfiguration> topicConfigurations(Model model, ServiceShape service) {
    List<TopicConfiguration> configurations = new ArrayList<>();
    for (OperationShape operation : operations(model, service)) {
      operation
          .findTrait(TraitIds.KAFKA_TOPIC_CONFIG)
          .ifPresent(
              trait -> {
                var node = trait.toNode().expectObjectNode();
                Map<String, String> topicConfiguration = new LinkedHashMap<>();
                addKafkaConfiguration(node, topicConfiguration, "retentionMs", "retention.ms");
                addKafkaConfiguration(
                    node, topicConfiguration, "retentionBytes", "retention.bytes");
                addKafkaConfiguration(
                    node, topicConfiguration, "minInsyncReplicas", "min.insync.replicas");
                addKafkaConfiguration(
                    node, topicConfiguration, "maxMessageBytes", "max.message.bytes");
                configurations.add(
                    new TopicConfiguration(
                        topicName(operation),
                        node.getNumberMember("partitions")
                            .map(value -> value.getValue().intValue())
                            .orElse(null),
                        node.getNumberMember("replicationFactor")
                            .map(value -> value.getValue().intValue())
                            .orElse(null),
                        topicConfiguration));
              });
    }
    return configurations;
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

  private static String topicName(OperationShape operation) {
    if (operation.hasTrait(TraitIds.KAFKA_PRODUCE)) {
      return topicName(operation, TraitIds.KAFKA_PRODUCE);
    }
    return topicName(operation, TraitIds.KAFKA_CONSUME);
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

  private static void addKafkaConfiguration(
      software.amazon.smithy.model.node.ObjectNode node,
      Map<String, String> configuration,
      String member,
      String kafkaName) {
    node.getNumberMember(member)
        .ifPresent(value -> configuration.put(kafkaName, value.getValue().toString()));
  }

  private static final ShapeId UNIT = ShapeId.from("smithy.api#Unit");

  private static boolean isUnit(ShapeId id) {
    return UNIT.equals(id);
  }
}

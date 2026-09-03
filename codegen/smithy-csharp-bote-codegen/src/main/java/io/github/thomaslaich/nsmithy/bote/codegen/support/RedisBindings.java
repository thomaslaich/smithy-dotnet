package io.github.thomaslaich.nsmithy.bote.codegen.support;

import io.github.thomaslaich.nsmithy.bote.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;

public final class RedisBindings {
  public record StreamAdd(
      String opName,
      String stream,
      Optional<Long> maxLen,
      StructureShape command,
      String commandType,
      Optional<StructureShape> reply,
      Optional<String> replyType) {}

  public record Subscription(
      String opName,
      String address,
      Optional<Long> maxLen,
      UnionShape union,
      String unionType,
      List<MemberShape> members) {}

  public record Publish(
      String opName, String channel, StructureShape command, String commandType) {}

  private RedisBindings() {}

  public static List<OperationShape> operations(Model model, ServiceShape service) {
    return TopDownIndex.of(model).getContainedOperations(service).stream()
        .sorted(Comparator.comparing(op -> op.getId().toString()))
        .toList();
  }

  public static List<StreamAdd> streamAdds(
      Model model, SymbolProvider symbols, ServiceShape service) {
    List<StreamAdd> result = new ArrayList<>();
    for (OperationShape operation : operations(model, service)) {
      if (!operation.hasTrait(TraitIds.REDIS_STREAM_ADD)) continue;
      StructureShape command = model.expectShape(operation.getInputShape(), StructureShape.class);
      StructureShape output = model.expectShape(operation.getOutputShape(), StructureShape.class);
      Optional<StructureShape> reply =
          output.hasTrait(TraitIds.REPLY) ? Optional.of(output) : Optional.empty();
      ObjectNode trait = trait(operation, TraitIds.REDIS_STREAM_ADD);
      result.add(
          new StreamAdd(
              CSharpNaming.typeName(operation.getId().getName()),
              stringMember(trait, "stream", operation),
              trait.getNumberMember("maxLen").map(number -> number.getValue().longValue()),
              command,
              qualified(symbols, command),
              reply,
              reply.map(shape -> qualified(symbols, shape))));
    }
    return result;
  }

  public static List<Subscription> streamReads(
      Model model, SymbolProvider symbols, ServiceShape service) {
    return subscriptions(model, symbols, service, TraitIds.REDIS_STREAM_READ, "stream");
  }

  public static List<Publish> publishes(Model model, SymbolProvider symbols, ServiceShape service) {
    List<Publish> result = new ArrayList<>();
    for (OperationShape operation : operations(model, service)) {
      if (!operation.hasTrait(TraitIds.REDIS_PUBLISH)) continue;
      StructureShape command = model.expectShape(operation.getInputShape(), StructureShape.class);
      ObjectNode trait = trait(operation, TraitIds.REDIS_PUBLISH);
      result.add(
          new Publish(
              CSharpNaming.typeName(operation.getId().getName()),
              stringMember(trait, "channel", operation),
              command,
              qualified(symbols, command)));
    }
    return result;
  }

  public static List<Subscription> subscribes(
      Model model, SymbolProvider symbols, ServiceShape service) {
    return subscriptions(model, symbols, service, TraitIds.REDIS_SUBSCRIBE, "channel");
  }

  private static List<Subscription> subscriptions(
      Model model,
      SymbolProvider symbols,
      ServiceShape service,
      ShapeId traitId,
      String addressMember) {
    List<Subscription> result = new ArrayList<>();
    for (OperationShape operation : operations(model, service)) {
      if (!operation.hasTrait(traitId)) continue;
      StructureShape output = model.expectShape(operation.getOutputShape(), StructureShape.class);
      MemberShape streaming =
          output.members().stream()
              .filter(member -> model.expectShape(member.getTarget()).hasTrait(TraitIds.STREAMING))
              .findFirst()
              .orElseThrow(
                  () ->
                      new IllegalStateException(
                          "@"
                              + traitId.getName()
                              + " output has no @streaming union: "
                              + operation.getId()));
      UnionShape union = model.expectShape(streaming.getTarget(), UnionShape.class);
      ObjectNode trait = trait(operation, traitId);
      result.add(
          new Subscription(
              CSharpNaming.typeName(operation.getId().getName()),
              stringMember(trait, addressMember, operation),
              trait.getNumberMember("maxLen").map(number -> number.getValue().longValue()),
              union,
              qualified(symbols, union),
              union.members().stream()
                  .sorted(Comparator.comparing(MemberShape::getMemberName))
                  .toList()));
    }
    return result;
  }

  private static String stringMember(ObjectNode trait, String member, OperationShape operation) {
    return trait
        .getStringMember(member)
        .orElseThrow(
            () -> new IllegalStateException("Missing " + member + " on " + operation.getId()))
        .getValue();
  }

  private static ObjectNode trait(OperationShape operation, ShapeId traitId) {
    return operation
        .findTrait(traitId)
        .orElseThrow(
            () -> new IllegalStateException("Missing @" + traitId + " on " + operation.getId()))
        .toNode()
        .expectObjectNode();
  }

  private static String qualified(
      SymbolProvider symbols, software.amazon.smithy.model.shapes.Shape shape) {
    return CSharpSymbolProvider.qualified(symbols.toSymbol(shape));
  }
}

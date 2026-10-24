package io.github.thomaslaich.nsmithy.csharp.codegen.validation;

import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.validation.AbstractValidator;
import software.amazon.smithy.model.validation.ValidationEvent;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * gRPC streams a message type rather than a structure, so the input or output of an event-stream
 * operation on a gRPC service holds its event stream and nothing else.
 */
@SmithyInternalApi
public final class GrpcEventStreamValidator extends AbstractValidator {

  @Override
  public List<ValidationEvent> validate(Model model) {
    List<ValidationEvent> events = new ArrayList<>();
    TopDownIndex index = TopDownIndex.of(model);
    for (ServiceShape service : model.getServiceShapes()) {
      if (!service.hasTrait(TraitIds.GRPC)) {
        continue;
      }
      for (OperationShape operation : index.getContainedOperations(service)) {
        validate(model, operation, operation.getInputShape(), "input", events);
        validate(model, operation, operation.getOutputShape(), "output", events);
      }
    }
    return events;
  }

  private void validate(
      Model model,
      OperationShape operation,
      ShapeId shapeId,
      String direction,
      List<ValidationEvent> events) {
    if (!(model.expectShape(shapeId) instanceof StructureShape structure)
        || !ShapeSupport.isEventStreamShape(model, shapeId)
        || structure.members().size() == 1) {
      return;
    }

    String members =
        structure.members().stream()
            .sorted(Comparator.comparing(MemberShape::getMemberName))
            .map(
                member ->
                    member.getMemberName()
                        + (ShapeSupport.isEventStreamMember(model, member)
                            ? " (event stream)"
                            : ""))
            .collect(Collectors.joining(", "));
    events.add(
        error(
            operation,
            "gRPC event-stream operation "
                + operation.getId()
                + " "
                + direction
                + " shape "
                + shapeId
                + " must contain exactly one event-stream member. gRPC streams the event type"
                + " itself, so sibling members cannot be represented. Members: "
                + members
                + "."));
  }
}

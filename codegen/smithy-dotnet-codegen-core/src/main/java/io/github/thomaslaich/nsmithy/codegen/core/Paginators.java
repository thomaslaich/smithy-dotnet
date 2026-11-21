package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginatedIndex;
import software.amazon.smithy.model.knowledge.PaginationInfo;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Which operations get paginators. A client pages through an operation by calling it again with
 * each response's continuation token until a response has none.
 */
@SmithyInternalApi
public final class Paginators {

  private Paginators() {}

  /**
   * An operation's resolved {@code @paginated} trait, merged with the service's. Event-stream
   * operations get no paginator, nor does one whose pages paginator would share its name with
   * another operation, {@code {Op}Pages}.
   */
  public static Optional<PaginationInfo> of(
      Model model, ServiceShape service, OperationShape operation) {
    if (ShapeSupport.isEventStreamOperation(model, operation)
        || collides(model, service, operation, "Pages")) {
      return Optional.empty();
    }
    return PaginatedIndex.of(model).getPaginationInfo(service, operation);
  }

  /**
   * The list the {@code items} member names, which an items paginator flattens. Empty when the
   * trait names no items, they are a map, which only the pages paginator covers, or another
   * operation is named {@code {Op}Items}.
   */
  public static Optional<ListShape> items(Model model, PaginationInfo info) {
    List<MemberShape> path = info.getItemsMemberPath();
    if (path.isEmpty() || collides(model, info.getService(), info.getOperation(), "Items")) {
      return Optional.empty();
    }
    return model.expectShape(path.get(path.size() - 1).getTarget()).asListShape();
  }

  /** Whether both continuation tokens are strings, not the discouraged maps. */
  public static boolean hasStringTokens(Model model, PaginationInfo info) {
    List<MemberShape> output = info.getOutputTokenMemberPath();
    return model.expectShape(info.getInputTokenMember().getTarget()).isStringShape()
        && model.expectShape(output.get(output.size() - 1).getTarget()).isStringShape();
  }

  /** Whether the service has an operation named like the operation's paginator. */
  private static boolean collides(
      Model model, ServiceShape service, OperationShape operation, String suffix) {
    String name = operation.getId().getName() + suffix;
    return TopDownIndex.of(model).getContainedOperations(service).stream()
        .anyMatch(other -> other.getId().getName().equalsIgnoreCase(name));
  }
}

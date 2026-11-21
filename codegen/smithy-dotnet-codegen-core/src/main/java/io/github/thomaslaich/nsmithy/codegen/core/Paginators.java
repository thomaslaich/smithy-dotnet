package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginatedIndex;
import software.amazon.smithy.model.knowledge.PaginationInfo;
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
   * operations get no paginator.
   */
  public static Optional<PaginationInfo> of(
      Model model, ServiceShape service, OperationShape operation) {
    if (ShapeSupport.isEventStreamOperation(model, operation)) {
      return Optional.empty();
    }
    return PaginatedIndex.of(model).getPaginationInfo(service, operation);
  }

  /**
   * The list the {@code items} member names, which an items paginator flattens. Empty when the
   * trait names no items or they are a map, which only the pages paginator covers.
   */
  public static Optional<ListShape> items(Model model, PaginationInfo info) {
    List<MemberShape> path = info.getItemsMemberPath();
    if (path.isEmpty()) {
      return Optional.empty();
    }
    return model.expectShape(path.get(path.size() - 1).getTarget()).asListShape();
  }
}

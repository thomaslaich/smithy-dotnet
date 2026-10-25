package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.ShapeClosureIndex;
import software.amazon.smithy.model.metadata.ShapeClosure;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Selects the shapes a generator generates: the configured {@code shapeClosures} entry, the
 * service, or both, plus any shapes integrations add to the service.
 */
@SmithyInternalApi
public final class ShapeClosures {

  /** Id of the closure generated when integrations add shapes to the service. */
  private static final String GENERATED_CLOSURE = "nsmithy.generated";

  private ShapeClosures() {}

  /**
   * Points {@code runner} at the shapes to generate. A closure without a service generates types
   * only: operations and services it contains get no client or server, since none of them is the
   * service being generated.
   */
  public static void configure(
      CodegenDirector<?, ?, ?, ?> runner,
      Model model,
      Optional<ServiceShape> service,
      Optional<String> closure,
      Set<ShapeId> additionalShapes) {
    service.ifPresent(shape -> runner.service(shape.getId()));
    Optional<ShapeClosure> configured = closure.map(id -> configuredClosure(model, id));
    // A shape the configured closure already contains needs no selector of its own.
    configured.ifPresent(
        c ->
            ShapeClosureIndex.of(model)
                .getShapesInClosure(c.getId())
                .forEach(shape -> additionalShapes.remove(shape.getId())));
    if (additionalShapes.isEmpty()) {
      configured.ifPresent(c -> runner.shapeClosure(c.getId()));
    } else {
      runner.shapeClosure(generatedClosure(model, service.get(), configured, additionalShapes));
    }
    if (service.isEmpty()) {
      runner.generateDataShapesOnly();
    }
  }

  /**
   * The closure generated when integrations add shapes to a service: the configured closure, or the
   * service when there is none, plus the added shapes it does not already contain.
   */
  private static ShapeClosure generatedClosure(
      Model model,
      ServiceShape service,
      Optional<ShapeClosure> configured,
      Set<ShapeId> additionalShapes) {
    ShapeClosure.Builder closure = ShapeClosure.builder().id(GENERATED_CLOSURE);
    List<String> selectors = new ArrayList<>();
    if (configured.isPresent()) {
      ShapeClosure base = configured.get();
      // The generated closure has another id, so check here what the director would check for the
      // configured one.
      if (!ShapeClosureIndex.of(model).getShapesInClosure(base.getId()).contains(service)) {
        throw new CodegenException(
            "The service `"
                + service.getId()
                + "` must be a member of the shape closure `"
                + base.getId()
                + "` when both are set, but it is not part of that closure.");
      }
      closure.includeNamespaces(base.getIncludeNamespaces()).rename(base.getRename());
      base.getIncludeBySelector().ifPresent(selectors::add);
    } else {
      selectors.add(idSelector(service.getId()));
    }
    additionalShapes.forEach(id -> selectors.add(idSelector(id)));
    return closure.includeBySelector(":is(" + String.join(", ", selectors) + ")").build();
  }

  private static ShapeClosure configuredClosure(Model model, String id) {
    ShapeClosure configured = ShapeClosure.fromModel(model).get(id);
    if (configured == null) {
      throw new CodegenException(
          "The shape closure `" + id + "` is not defined in the model's shapeClosures metadata.");
    }
    return configured;
  }

  private static String idSelector(ShapeId id) {
    return "[id = '" + id + "']";
  }
}

package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import software.amazon.smithy.build.ProjectionTransformer;
import software.amazon.smithy.build.SmithyBuildException;
import software.amazon.smithy.build.TransformContext;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * The {@code declareValidationException} build transform: declares {@code
 * smithy.framework#ValidationException} on every operation whose input is constrained, for a model
 * the project does not own and so cannot change. The model must define the framework's shapes,
 * which the Maven dependency {@code software.amazon.smithy:smithy-validation-model} brings.
 *
 * <pre>
 * "projections": {
 *   "server": {
 *     "transforms": [{ "name": "declareValidationException" }],
 *     "plugins": { "csharp-codegen": { "service": "example.weather#Weather" } }
 *   }
 * }
 * </pre>
 */
@SmithyUnstableApi
public final class DeclareValidationException implements ProjectionTransformer {

  @Override
  public String getName() {
    return "declareValidationException";
  }

  @Override
  public Model transform(TransformContext context) {
    Model model = context.getModel();
    if (model.getShape(ShapeSupport.VALIDATION_EXCEPTION).isEmpty()) {
      throw new SmithyBuildException(
          "The declareValidationException transform needs smithy.framework#ValidationException"
              + " in the model. Add the Maven dependency"
              + " software.amazon.smithy:smithy-validation-model to smithy-build.json.");
    }
    Set<ShapeId> declaredByService = new HashSet<>();
    TopDownIndex topDown = TopDownIndex.of(model);
    for (ServiceShape service : model.getServiceShapes()) {
      if (service.getIntroducedErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)) {
        topDown
            .getContainedOperations(service)
            .forEach(operation -> declaredByService.add(operation.getId()));
      }
    }
    List<Shape> declared = new ArrayList<>();
    for (OperationShape operation : model.getOperationShapes()) {
      if (operation.getIntroducedErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)
          || declaredByService.contains(operation.getId())
          || !ValidationErrors.isConstrained(model, operation)) {
        continue;
      }
      declared.add(operation.toBuilder().addError(ShapeSupport.VALIDATION_EXCEPTION).build());
    }
    return declared.isEmpty() ? model : context.getTransformer().replaceShapes(model, declared);
  }
}

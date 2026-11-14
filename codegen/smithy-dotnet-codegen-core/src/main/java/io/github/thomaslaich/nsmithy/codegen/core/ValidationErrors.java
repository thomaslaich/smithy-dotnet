package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.neighbor.Walker;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.EnumTrait;
import software.amazon.smithy.model.traits.LengthTrait;
import software.amazon.smithy.model.traits.PatternTrait;
import software.amazon.smithy.model.traits.RangeTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.model.traits.UniqueItemsTrait;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * {@code smithy.framework#ValidationException} on the operations that can return it.
 *
 * <p>The server runtime answers a request that violates the constraint traits of an operation's
 * input with that error. The model is the contract for every client of the service, whatever
 * generates it, so a server requires the model to declare the error on every operation whose input
 * is constrained, itself or through its service; a client generates exactly what the model
 * declares. The framework's shapes come from the Maven dependency {@code
 * software.amazon.smithy:smithy-validation-model} and are never generated: the generators bind them
 * to the runtime's {@code ValidationException} and {@code ValidationExceptionField}.
 *
 * <p>For a model the project does not own, the {@code attachValidationException} setting declares
 * the error on every constrained operation of the model the generator runs on.
 */
@SmithyInternalApi
public final class ValidationErrors {

  /** {@code smithy.framework#ValidationExceptionField}, one violation of the input. */
  public static final ShapeId FIELD = ShapeId.from("smithy.framework#ValidationExceptionField");

  /** {@code smithy.framework#ValidationExceptionFieldList}, the violations of a request. */
  public static final ShapeId FIELD_LIST =
      ShapeId.from("smithy.framework#ValidationExceptionFieldList");

  /**
   * The constraints the server runtime enforces; see {@code ConstraintTraits} in
   * NSmithy.Core.Validation. Enum shapes are checked for membership too.
   */
  @SuppressWarnings("deprecation") // the deprecated @enum trait is still enforced
  private static final Set<ShapeId> ENFORCED_TRAITS =
      Set.of(
          RequiredTrait.ID,
          LengthTrait.ID,
          RangeTrait.ID,
          PatternTrait.ID,
          UniqueItemsTrait.ID,
          EnumTrait.ID);

  private ValidationErrors() {}

  /**
   * Whether the runtime owns a shape of the validation model: the error itself, a field of it, or
   * the list of fields. Generators bind these to the runtime's types instead of generating them.
   */
  public static boolean isFrameworkShape(ShapeId id) {
    return ShapeSupport.isValidationException(id) || FIELD.equals(id) || FIELD_LIST.equals(id);
  }

  /**
   * Whether the server validates the operation's input: a constraint the runtime enforces, or an
   * enum shape, is reachable from it. An operation with no input is never validated.
   */
  public static boolean isConstrained(Model model, OperationShape operation) {
    if (ShapeSupport.isUnit(operation.getInputShape())) {
      return false;
    }
    Shape input = model.expectShape(operation.getInputShape());
    return new Walker(model).walkShapes(input).stream().anyMatch(ValidationErrors::constrains);
  }

  private static boolean constrains(Shape shape) {
    return shape.getType() == ShapeType.ENUM || ENFORCED_TRAITS.stream().anyMatch(shape::hasTrait);
  }

  /**
   * The model a generator runs on: with the error declared on every constrained operation when
   * {@code attach} is set, and otherwise checked, for a server, so that the service's constrained
   * operations declare it.
   */
  public static Model apply(
      Model model, Optional<ShapeId> service, boolean attach, boolean generateServer) {
    if (attach) {
      return attach(model);
    }
    if (generateServer) {
      service.ifPresent(id -> requireDeclared(model, model.expectShape(id, ServiceShape.class)));
    }
    return model;
  }

  /**
   * The model with {@code smithy.framework#ValidationException} declared on every operation whose
   * input is constrained. An operation that declares it, or whose service declares it for every
   * operation, is left alone, and so is one the server never validates. The model must define the
   * framework's shapes.
   */
  public static Model attach(Model model) {
    if (model.getShape(ShapeSupport.VALIDATION_EXCEPTION).isEmpty()) {
      throw new CodegenException(
          "attachValidationException needs smithy.framework#ValidationException in the model. Add"
              + " the Maven dependency software.amazon.smithy:smithy-validation-model to"
              + " smithy-build.json.");
    }
    Set<ShapeId> declaredByService = new HashSet<>();
    TopDownIndex topDown = TopDownIndex.of(model);
    for (ServiceShape service : model.getServiceShapes()) {
      if (service.getErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)) {
        topDown
            .getContainedOperations(service)
            .forEach(operation -> declaredByService.add(operation.getId()));
      }
    }
    List<Shape> attached = new ArrayList<>();
    for (OperationShape operation : model.getOperationShapes()) {
      if (operation.getErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)
          || declaredByService.contains(operation.getId())
          || !isConstrained(model, operation)) {
        continue;
      }
      attached.add(operation.toBuilder().addError(ShapeSupport.VALIDATION_EXCEPTION).build());
    }
    return attached.isEmpty() ? model : ModelTransformer.create().replaceShapes(model, attached);
  }

  /** Whether the operation declares the error, itself or through the service. */
  public static boolean declares(OperationShape operation, ServiceShape service) {
    return operation.getErrors(service).contains(ShapeSupport.VALIDATION_EXCEPTION);
  }

  /**
   * Fails when a constrained operation of the service does not declare {@code
   * smithy.framework#ValidationException}, itself or through the service. The server returns the
   * error regardless, so the model has to say so.
   */
  public static void requireDeclared(Model model, ServiceShape service) {
    List<OperationShape> undeclared =
        TopDownIndex.of(model).getContainedOperations(service).stream()
            .filter(operation -> !declares(operation, service))
            .filter(operation -> isConstrained(model, operation))
            .sorted()
            .toList();
    if (undeclared.isEmpty()) {
      return;
    }
    String ids =
        undeclared.stream()
            .map(operation -> operation.getId().toString())
            .collect(Collectors.joining(", "));
    String example = undeclared.get(0).getId().getName();
    throw new CodegenException(
        (undeclared.size() == 1 ? "Operation " : "Operations ")
            + ids
            + (undeclared.size() == 1 ? " takes" : " take")
            + " input that is constrained, so the server answers a request that violates a"
            + " constraint with smithy.framework#ValidationException, which the model does not"
            + " declare. Declare it on each such operation, or on the service for every"
            + " operation:\n\n"
            + "    use smithy.framework#ValidationException\n\n"
            + "    operation "
            + example
            + " {\n"
            + "        ...\n"
            + "        errors: [ValidationException]\n"
            + "    }\n\n"
            + "The shape comes from the Maven dependency"
            + " software.amazon.smithy:smithy-validation-model. For a model you do not own, set"
            + " attachValidationException to true in the plugin settings and the generator declares"
            + " it. See"
            + " https://thomaslaich.github.io/smithy-dotnet/servers/validation/#declaring-the-error.");
  }
}

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
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.EnumTrait;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.HttpErrorTrait;
import software.amazon.smithy.model.traits.LengthTrait;
import software.amazon.smithy.model.traits.PatternTrait;
import software.amazon.smithy.model.traits.RangeTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.model.traits.UniqueItemsTrait;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Declares {@code smithy.framework#ValidationException} on the operations that can return it.
 *
 * <p>The server runtime answers a request that violates the constraint traits of an operation's
 * input with that error whether or not the model declares it. So that the generated code tells the
 * truth about it, the generators declare the error on every operation whose input is constrained,
 * as smithy-rs does: a C# client documents it, an F# client returns it as a case of the operation's
 * error union, and the schemas list it. A project that would rather declare the error in its model
 * turns {@code attachValidationException} off, and a server then refuses to generate a constrained
 * operation that does not declare it.
 *
 * <p>The framework shapes come from the {@code smithy-validation-model} package, which few models
 * depend on, so they are added to the model when it lacks them. The generators never generate them:
 * they bind to the runtime's {@code ValidationException} and {@code ValidationExceptionField}.
 */
@SmithyInternalApi
public final class ValidationErrors {

  /** {@code smithy.framework#ValidationExceptionField}, one violation of the input. */
  public static final ShapeId FIELD = ShapeId.from("smithy.framework#ValidationExceptionField");

  /** {@code smithy.framework#ValidationExceptionFieldList}, the violations of a request. */
  public static final ShapeId FIELD_LIST =
      ShapeId.from("smithy.framework#ValidationExceptionFieldList");

  private static final ShapeId STRING = ShapeId.from("smithy.api#String");

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
   * The model a generator runs on: with the error attached when {@code attach} is set, and
   * otherwise checked, for a server, so that the service's constrained operations declare it.
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
   * operation, is left alone, and so is one the server never validates. The framework's shapes are
   * added when the model has none.
   */
  public static Model attach(Model model) {
    Model withShapes = withFrameworkShapes(model);
    Set<ShapeId> declaredByService = new HashSet<>();
    TopDownIndex topDown = TopDownIndex.of(withShapes);
    for (ServiceShape service : withShapes.getServiceShapes()) {
      if (service.getErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)) {
        topDown
            .getContainedOperations(service)
            .forEach(operation -> declaredByService.add(operation.getId()));
      }
    }
    List<Shape> attached = new ArrayList<>();
    for (OperationShape operation : withShapes.getOperationShapes()) {
      if (operation.getErrors().contains(ShapeSupport.VALIDATION_EXCEPTION)
          || declaredByService.contains(operation.getId())
          || !isConstrained(withShapes, operation)) {
        continue;
      }
      attached.add(operation.toBuilder().addError(ShapeSupport.VALIDATION_EXCEPTION).build());
    }
    return attached.isEmpty()
        ? withShapes
        : ModelTransformer.create().replaceShapes(withShapes, attached);
  }

  /**
   * Fails when a constrained operation of the service does not declare {@code
   * smithy.framework#ValidationException}, itself or through the service. The server returns the
   * error regardless, so a model that opts out of having it attached must declare it.
   */
  public static void requireDeclared(Model model, ServiceShape service) {
    List<OperationShape> undeclared =
        TopDownIndex.of(model).getContainedOperations(service).stream()
            .filter(operation ->
                !operation.getErrors(service).contains(ShapeSupport.VALIDATION_EXCEPTION))
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
            + " software.amazon.smithy:smithy-validation-model. Or leave attachValidationException"
            + " at its default, true, and the generator declares it for you. See"
            + " https://thomaslaich.github.io/smithy-dotnet/servers/validation/.");
  }

  /** The model with the framework's validation shapes, added when it has none of its own. */
  private static Model withFrameworkShapes(Model model) {
    if (model.getShape(ShapeSupport.VALIDATION_EXCEPTION).isPresent()) {
      return model;
    }
    StructureShape field =
        StructureShape.builder()
            .id(FIELD)
            .addTrait(
                new DocumentationTrait(
                    "Describes one specific validation failure for an input member."))
            .addMember(
                requiredString(
                    FIELD.withMember("path"),
                    "A JSONPointer expression to the structure member whose value failed to satisfy"
                        + " the modeled constraints."))
            .addMember(
                requiredString(
                    FIELD.withMember("message"),
                    "A detailed description of the validation failure."))
            .build();
    ListShape fieldList =
        ListShape.builder()
            .id(FIELD_LIST)
            .member(MemberShape.builder().id(FIELD_LIST.withMember("member")).target(FIELD).build())
            .build();
    StructureShape exception =
        StructureShape.builder()
            .id(ShapeSupport.VALIDATION_EXCEPTION)
            .addTrait(new ErrorTrait("client"))
            .addTrait(new HttpErrorTrait(400))
            .addTrait(
                new DocumentationTrait(
                    "A standard error for input validation failures. This should be thrown by"
                        + " services when a member of the input structure falls outside of the"
                        + " modeled or documented constraints."))
            .addMember(
                requiredString(
                    ShapeSupport.VALIDATION_EXCEPTION.withMember("message"),
                    "A summary of the validation failure."))
            .addMember(
                MemberShape.builder()
                    .id(ShapeSupport.VALIDATION_EXCEPTION.withMember("fieldList"))
                    .target(FIELD_LIST)
                    .addTrait(
                        new DocumentationTrait(
                            "A list of specific failures encountered while validating the input."
                                + " A member can appear in this list more than once if it failed"
                                + " to satisfy multiple constraints."))
                    .build())
            .build();
    return model.toBuilder().addShapes(field, fieldList, exception).build();
  }

  private static MemberShape requiredString(ShapeId id, String documentation) {
    return MemberShape.builder()
        .id(id)
        .target(STRING)
        .addTrait(new RequiredTrait())
        .addTrait(new DocumentationTrait(documentation))
        .build();
  }
}

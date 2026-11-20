package io.github.thomaslaich.nsmithy.fsharp.codegen;

import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.utils.SmithyInternalApi;

/** The types generated code declares, which no type generated for a model shape may share. */
@SmithyInternalApi
final class GeneratedNames {

  private GeneratedNames() {}

  /**
   * Fails when a model shape's F# type would have one of {@code names} in the service's namespace.
   */
  static void check(FSharpContext context, ServiceShape service, String... names) {
    String namespace = context.settings().fsharpNamespace(service.getId().getNamespace());
    for (String name : names) {
      declaring(context, namespace, name)
          .ifPresent(shape -> {
            throw new CodegenException(
                "Shape "
                    + shape.getId()
                    + " would generate an F# type named "
                    + name
                    + ", which the generated code for "
                    + service.getId()
                    + " declares. Rename the shape, for example with the renameShapes build"
                    + " transform.");
          });
    }
  }

  /**
   * The model shape whose generated F# type is {@code namespace.name}, if any. Only structures,
   * unions, and enums declare types; other shapes use F# or runtime types.
   */
  static Optional<Shape> declaring(FSharpContext context, String namespace, String name) {
    String fullName = namespace + "." + name;
    return context
        .model()
        .shapes()
        .filter(GeneratedNames::declaresType)
        .filter(shape -> context.symbolProvider().toSymbol(shape).getFullName().equals(fullName))
        .findFirst();
  }

  private static boolean declaresType(Shape shape) {
    ShapeType type = shape.getType();
    return type == ShapeType.STRUCTURE
        || type == ShapeType.UNION
        || type == ShapeType.ENUM
        || type == ShapeType.INT_ENUM;
  }
}

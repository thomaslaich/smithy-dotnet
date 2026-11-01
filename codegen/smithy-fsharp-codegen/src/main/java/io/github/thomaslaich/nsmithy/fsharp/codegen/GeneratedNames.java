package io.github.thomaslaich.nsmithy.fsharp.codegen;

import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/** The types a service's client and server declare, which no model type may share a name with. */
@SmithyInternalApi
final class GeneratedNames {

  private GeneratedNames() {}

  /** Fails when a model type of the service's namespace would have one of {@code names}. */
  static void check(Model model, ServiceShape service, String... names) {
    String namespace = service.getId().getNamespace();
    for (String name : names) {
      model
          .shapes()
          .filter(shape -> !shape.isMemberShape())
          .filter(shape -> shape.getId().getNamespace().equals(namespace))
          .filter(shape -> FSharpNaming.typeName(shape.getId().getName()).equals(name))
          .findFirst()
          .ifPresent(
              shape -> {
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
}

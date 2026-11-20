package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ProvidedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.Map;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The types generated code declares besides those of model shapes, such as an operation's error
 * union, which no type of a model shape may share. A generator reserves its names as it goes; once
 * every shape is generated, they are checked against the types the run generated and those its
 * references provide, so a shape that is neither does not count.
 */
@SmithyInternalApi
final class GeneratedNames {

  /** Each reserved type's full name, and what declares it, as the end of a sentence. */
  private final Map<String, String> reserved = new LinkedHashMap<>();

  /**
   * Reserves {@code namespace.name}, which {@code declaredBy} ("holds the errors of ...") explains.
   */
  void reserve(String namespace, String name, String declaredBy) {
    reserved.putIfAbsent(namespace + "." + name, declaredBy);
  }

  /** Fails when a type the run generated, or one its references provide, has a reserved name. */
  void check(ShapeManifest generated, ProvidedShapes provided) {
    Map<String, ShapeId> types = new HashMap<>();
    generated
        .shapes()
        .forEach((id, symbols) -> {
          String type = symbols.get(ShapeManifest.TYPE);
          if (type != null) {
            types.putIfAbsent(type, id);
          }
        });
    provided
        .ids()
        .forEach(
            id -> provided.type(id).ifPresent(type -> types.putIfAbsent(type.getFullName(), id)));
    reserved.forEach((fullName, declaredBy) -> {
      ShapeId shape = types.get(fullName);
      if (shape != null) {
        throw new CodegenException(
            "Shape "
                + shape
                + " would generate an F# type named "
                + fullName.substring(fullName.lastIndexOf('.') + 1)
                + ", which "
                + declaredBy
                + ". Rename the shape, for example with the renameShapes build transform.");
      }
    });
  }
}

package io.github.thomaslaich.nsmithy.codegen.core;

import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.TreeSet;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.loader.Prelude;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.TraitDefinition;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The shapes a project owns: those defined in its own model files. A project generates the shapes
 * it owns; shapes defined elsewhere, such as in a referenced project's model, belong to whoever
 * defines them.
 */
@SmithyInternalApi
public final class OwnedShapes {

  private OwnedShapes() {}

  /**
   * The shapes defined in {@code sources}, files or directories of model files. Members, services,
   * resources, trait definitions, and prelude shapes are left out: members come with their
   * containers, services are generated only when a project names one, and the others generate no
   * code.
   */
  public static Set<ShapeId> of(Model model, List<String> sources) {
    List<Path> roots = sources.stream().map(OwnedShapes::normalize).toList();
    Set<ShapeId> owned = new TreeSet<>();
    for (Shape shape : model.toSet()) {
      if (isCandidate(shape) && definedIn(shape, roots)) {
        owned.add(shape.getId());
      }
    }
    return owned;
  }

  private static boolean isCandidate(Shape shape) {
    if (shape.isMemberShape() || Prelude.isPreludeShape(shape)) {
      return false;
    }
    if (shape.hasTrait(TraitDefinition.ID)) {
      return false;
    }
    ShapeType type = shape.getType();
    return type != ShapeType.SERVICE && type != ShapeType.RESOURCE;
  }

  private static boolean definedIn(Shape shape, List<Path> roots) {
    Optional<Path> file = file(shape.getSourceLocation().getFilename());
    return file.isPresent() && roots.stream().anyMatch(root -> file.get().startsWith(root));
  }

  /** The model file a shape was loaded from, or empty for shapes from JARs and synthetic ones. */
  private static Optional<Path> file(String filename) {
    if (filename == null || filename.isEmpty() || filename.startsWith("jar:")) {
      return Optional.empty();
    }
    try {
      return Optional.of(normalize(filename));
    } catch (InvalidPathException e) {
      return Optional.empty();
    }
  }

  private static Path normalize(String path) {
    return Path.of(path).toAbsolutePath().normalize();
  }
}

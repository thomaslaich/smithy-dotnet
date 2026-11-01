package io.github.thomaslaich.nsmithy.contracts;

import java.net.URI;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.NavigableSet;
import java.util.Optional;
import java.util.TreeMap;
import java.util.TreeSet;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.loader.Prelude;
import software.amazon.smithy.model.neighbor.NeighborProvider;
import software.amazon.smithy.model.neighbor.Relationship;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;

/**
 * Derives a contract's Maven dependencies from the model.
 *
 * <p>A plugin cannot see the build file's {@code maven} block, so the dependencies come from the
 * model itself: every non-prelude shape or trait a source shape references must have been loaded
 * from a JAR, and that JAR's path in a Maven-layout cache gives its coordinate. This declares only
 * what the model uses, not everything on the build classpath.
 */
final class DependencyResolver {

  private DependencyResolver() {}

  static NavigableSet<String> resolve(PluginContext context) {
    return resolve(context, cacheRoots());
  }

  static NavigableSet<String> resolve(PluginContext context, List<Path> cacheRoots) {
    Model model = context.getModel();
    NeighborProvider neighbors =
        NeighborProvider.withTraitRelationships(model, NeighborProvider.of(model));

    // Jar path -> one referencing shape, for error messages.
    TreeMap<Path, ShapeId> jars = new TreeMap<>();
    TreeMap<String, ShapeId> looseFiles = new TreeMap<>();
    for (Shape shape : model.toSet()) {
      if (!context.isSourceShape(shape)) {
        continue;
      }
      for (Relationship relationship : neighbors.getNeighbors(shape)) {
        ShapeId target = relationship.getNeighborShapeId();
        if (Prelude.isPreludeShape(target) || context.isSourceShape(target)) {
          continue;
        }
        Optional<Shape> targetShape = model.getShape(target);
        if (targetShape.isEmpty()) {
          continue;
        }
        String filename = targetShape.get().getSourceLocation().getFilename();
        Optional<Path> jar = jarOf(filename);
        if (jar.isPresent()) {
          jars.putIfAbsent(jar.get(), shape.getId());
        } else {
          looseFiles.putIfAbsent(filename, target);
        }
      }
    }

    if (!looseFiles.isEmpty()) {
      throw new SmithyBuildException(
          "contracts-jar: the model references shapes from files that are neither "
              + "sources nor a JAR, so a consumer of the contract could not load them: "
              + looseFiles
              + ". Add those files to `sources`, or package them as their own contract.");
    }

    NavigableSet<String> coordinates = new TreeSet<>();
    List<String> unresolved = new ArrayList<>();
    jars.forEach(
        (jar, user) ->
            coordinateOf(jar, cacheRoots)
                .ifPresentOrElse(
                    coordinates::add, () -> unresolved.add(jar + " (used by " + user + ")")));
    if (!unresolved.isEmpty()) {
      throw new SmithyBuildException(
          "contracts-jar: cannot determine the Maven coordinates of "
              + unresolved
              + ". Declare the contract's dependencies explicitly with the `dependencies`"
              + " setting.");
    }
    return coordinates;
  }

  /** The JAR a model file was loaded from, if it was: {@code jar:file:/x.jar!/META-INF/...}. */
  static Optional<Path> jarOf(String filename) {
    if (!filename.startsWith("jar:")) {
      return Optional.empty();
    }
    int separator = filename.indexOf("!/");
    if (separator < 0) {
      return Optional.empty();
    }
    try {
      return Optional.of(Path.of(URI.create(filename.substring("jar:".length(), separator))));
    } catch (IllegalArgumentException e) {
      return Optional.empty();
    }
  }

  /**
   * The coordinate of a JAR in a Maven-layout cache ({@code
   * <root>/<group/path>/<a>/<v>/<a>-<v>.jar}) or a Gradle module cache ({@code
   * files-2.1/<group>/<a>/<v>/<hash>/<a>-<v>.jar}).
   */
  static Optional<String> coordinateOf(Path jar, List<Path> cacheRoots) {
    Path file = jar.toAbsolutePath().normalize();
    String name = file.getFileName().toString();

    Path versionDir = file.getParent();
    if (versionDir != null && versionDir.getParent() != null) {
      String version = versionDir.getFileName().toString();
      Path artifactDir = versionDir.getParent();
      String artifact = artifactDir.getFileName().toString();
      Path groupDir = artifactDir.getParent();
      if (name.equals(artifact + "-" + version + ".jar") && groupDir != null) {
        for (Path root : cacheRoots) {
          Path normalizedRoot = root.toAbsolutePath().normalize();
          if (groupDir.startsWith(normalizedRoot) && !groupDir.equals(normalizedRoot)) {
            String group =
                normalizedRoot
                    .relativize(groupDir)
                    .toString()
                    .replace(groupDir.getFileSystem().getSeparator(), ".");
            return Optional.of(group + ":" + artifact + ":" + version);
          }
        }
      }
    }

    // Gradle: the version directory holds a hash directory, and the group is one dotted name.
    Path hashDir = file.getParent();
    Path gradleVersionDir = hashDir == null ? null : hashDir.getParent();
    Path gradleArtifactDir = gradleVersionDir == null ? null : gradleVersionDir.getParent();
    Path gradleGroupDir = gradleArtifactDir == null ? null : gradleArtifactDir.getParent();
    if (gradleGroupDir != null
        && gradleGroupDir.getParent() != null
        && gradleGroupDir.getParent().getFileName().toString().equals("files-2.1")) {
      String version = gradleVersionDir.getFileName().toString();
      String artifact = gradleArtifactDir.getFileName().toString();
      if (name.equals(artifact + "-" + version + ".jar")) {
        return Optional.of(gradleGroupDir.getFileName() + ":" + artifact + ":" + version);
      }
    }
    return Optional.empty();
  }

  /** Where the Smithy CLI resolves Maven dependencies to. */
  private static List<Path> cacheRoots() {
    List<Path> roots = new ArrayList<>();
    String cache = System.getenv("SMITHY_MAVEN_CACHE");
    if (cache != null && !cache.isBlank()) {
      roots.add(Path.of(cache));
    }
    roots.add(Path.of(System.getProperty("user.home"), ".m2", "repository"));
    return roots;
  }
}

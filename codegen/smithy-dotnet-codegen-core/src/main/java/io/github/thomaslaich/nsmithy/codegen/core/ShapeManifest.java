package io.github.thomaslaich.nsmithy.codegen.core;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.util.Map;
import java.util.Properties;
import java.util.TreeMap;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The shapes a code generation run generated, with the .NET symbols that represent them. Written as
 * {@value #FILE_NAME} next to the generated files, so that projects referencing this one can use
 * these types instead of generating the shapes again. See designs/shape-ownership.md.
 */
@SmithyInternalApi
public final class ShapeManifest {

  /** The manifest's file name in the plugin's output directory. */
  public static final String FILE_NAME = "nsmithy-shapes.json";

  /** The manifest format's version. */
  public static final int FORMAT_VERSION = 1;

  /** The generator name of smithy-csharp-codegen. */
  public static final String CSHARP_GENERATOR = "smithy-csharp-codegen";

  /** The generator name of smithy-fsharp-codegen. */
  public static final String FSHARP_GENERATOR = "smithy-fsharp-codegen";

  /** The type a shape is generated as. Lists and maps have none: they use runtime types. */
  public static final String TYPE = "type";

  /** The class holding a shape's schema. */
  public static final String SCHEMA = "schema";

  /** An operation's server handler interface. */
  public static final String HANDLER = "handler";

  private final String generator;
  private final Map<ShapeId, Map<String, String>> shapes = new TreeMap<>();

  /** An empty manifest for the shapes {@code generator} generates. */
  public ShapeManifest(String generator) {
    this.generator = generator;
  }

  /** Records that {@code shape} is represented by {@code symbol} in the given role. */
  public void add(ShapeId shape, String role, Symbol symbol) {
    shapes.computeIfAbsent(shape, id -> new TreeMap<>()).put(role, symbol.getFullName());
  }

  /** The recorded shapes and their symbols, by shape id. */
  public Map<ShapeId, Map<String, String>> shapes() {
    return shapes;
  }

  /**
   * Writes the manifest to {@code manifest}'s base directory, after checking that no recorded shape
   * is one the other generator's types already represent (see {@link
   * ProvidedShapes#checkNotForeign}).
   */
  public void write(FileManifest manifest, ProvidedShapes provided) {
    shapes.keySet().forEach(provided::checkNotForeign);
    ObjectNode.Builder shapeNodes = Node.objectNodeBuilder();
    shapes.forEach((id, symbols) -> {
      ObjectNode.Builder entry = Node.objectNodeBuilder();
      symbols.forEach(entry::withMember);
      shapeNodes.withMember(id.toString(), entry.build());
    });
    ObjectNode node =
        Node.objectNodeBuilder()
            .withMember("version", FORMAT_VERSION)
            .withMember("generator", generator)
            .withMember("generatorVersion", generatorVersion())
            .withMember("shapes", shapeNodes.build())
            .build();
    manifest.writeFile(FILE_NAME, Node.prettyPrintJson(node) + "\n");
  }

  /** The version of NSmithy's code generators, as built. */
  public static String generatorVersion() {
    try (InputStream in = ShapeManifest.class.getResourceAsStream("codegen-version.properties")) {
      if (in == null) {
        throw new IllegalStateException("codegen-version.properties is missing from the plugin.");
      }
      Properties properties = new Properties();
      properties.load(in);
      return properties.getProperty("version");
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }
  }
}

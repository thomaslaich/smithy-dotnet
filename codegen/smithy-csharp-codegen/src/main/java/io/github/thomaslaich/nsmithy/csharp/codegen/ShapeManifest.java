package io.github.thomaslaich.nsmithy.csharp.codegen;

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
 * The shapes a code generation run generated, with the C# symbols that represent them. Written as
 * {@value #FILE_NAME} next to the generated files, so that projects referencing this one can use
 * these types instead of generating the shapes again. See designs/shape-ownership.md.
 */
@SmithyInternalApi
public final class ShapeManifest {

  /** The manifest's file name in the plugin's output directory. */
  public static final String FILE_NAME = "nsmithy-shapes.json";

  /** The manifest format's version. */
  public static final int FORMAT_VERSION = 1;

  static final String GENERATOR = "smithy-csharp-codegen";

  /** The C# type a shape is generated as. Lists and maps have none: they use runtime types. */
  public static final String TYPE = "type";

  /** The class holding a shape's schema. */
  public static final String SCHEMA = "schema";

  /** An operation's server handler interface. */
  public static final String HANDLER = "handler";

  private final Map<ShapeId, Map<String, String>> shapes = new TreeMap<>();

  /** Records that {@code shape} is represented by {@code symbol} in the given role. */
  public void add(ShapeId shape, String role, Symbol symbol) {
    shapes.computeIfAbsent(shape, id -> new TreeMap<>()).put(role, symbol.getFullName());
  }

  /** The recorded shapes and their symbols, by shape id. */
  public Map<ShapeId, Map<String, String>> shapes() {
    return shapes;
  }

  /** Writes the manifest to {@code manifest}'s base directory. */
  public void write(FileManifest manifest) {
    ObjectNode.Builder shapeNodes = Node.objectNodeBuilder();
    shapes.forEach(
        (id, symbols) -> {
          ObjectNode.Builder entry = Node.objectNodeBuilder();
          symbols.forEach(entry::withMember);
          shapeNodes.withMember(id.toString(), entry.build());
        });
    ObjectNode node =
        Node.objectNodeBuilder()
            .withMember("version", FORMAT_VERSION)
            .withMember("generator", GENERATOR)
            .withMember("generatorVersion", generatorVersion())
            .withMember("shapes", shapeNodes.build())
            .build();
    manifest.writeFile(FILE_NAME, Node.prettyPrintJson(node) + "\n");
  }

  /** The version of this code generator, as built. */
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

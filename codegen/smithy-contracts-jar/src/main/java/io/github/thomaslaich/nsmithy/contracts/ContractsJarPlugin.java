package io.github.thomaslaich.nsmithy.contracts;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.NavigableSet;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.stream.Stream;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildException;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.build.plugins.SourcesPlugin;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;

/**
 * Smithy build plugin that packages a projection's model as a Maven JAR, POM, and checksums, so any
 * Smithy toolchain can consume the contract from a Maven registry.
 *
 * <p>Plugin name: {@code contracts-jar}. Add to {@code smithy-build.json}:
 *
 * <pre>{@code
 * "plugins": {
 *   "contracts-jar": {
 *     "groupId": "io.github.acme",
 *     "artifactId": "my-service-contracts",
 *     "version": "${CONTRACTS_VERSION}"
 *   }
 * }
 * }</pre>
 *
 * <p>The JAR holds what the built-in {@code sources} plugin emits for the projection (the source
 * files for {@code source}, the projected model otherwise) in Smithy's {@code META-INF/smithy}
 * discovery layout. The POM declares the Maven artifacts that provide the shapes and traits the
 * model's own shapes reference. Set {@code dependencies} to a list of {@code
 * groupId:artifactId:version} coordinates to declare them explicitly instead.
 *
 * <p>The plugin writes files only. Publish them with any Maven deploy tool.
 */
public final class ContractsJarPlugin implements SmithyBuildPlugin {

  private static final String JAR_PREFIX = "META-INF/smithy/";

  // Fixed entry time so the same model always produces the same JAR bytes.
  private static final long ENTRY_TIME = 315_532_800_000L; // 1980-01-01T00:00:00Z

  @Override
  public String getName() {
    return "contracts-jar";
  }

  @Override
  public void execute(PluginContext context) {
    ObjectNode settings = context.getSettings();
    String groupId = requiredString(settings, "groupId");
    String artifactId = requiredString(settings, "artifactId");
    String version = requiredString(settings, "version");

    NavigableSet<String> dependencies =
        settings
            .getArrayMember("dependencies")
            .map(ContractsJarPlugin::explicitDependencies)
            .orElseGet(() -> DependencyResolver.resolve(context));
    dependencies.remove(groupId + ":" + artifactId + ":" + version);

    String baseName = artifactId + "-" + version;
    FileManifest out = context.getFileManifest();
    byte[] jar = createJar(context);
    byte[] pom =
        Pom.render(groupId, artifactId, version, dependencies).getBytes(StandardCharsets.UTF_8);
    write(out, baseName + ".jar", jar);
    write(out, baseName + ".pom", pom);
  }

  private static byte[] createJar(PluginContext context) {
    Path staging;
    try {
      staging = Files.createTempDirectory("nsmithy-contracts-jar");
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }
    try {
      new SourcesPlugin()
          .execute(context.toBuilder().fileManifest(FileManifest.create(staging)).build());
      return zip(staging);
    } finally {
      deleteRecursively(staging);
    }
  }

  private static byte[] zip(Path root) {
    Map<String, Path> entries = new TreeMap<>();
    try (Stream<Path> files = Files.walk(root)) {
      files
          .filter(Files::isRegularFile)
          .forEach(
              f -> entries.put(JAR_PREFIX + root.relativize(f).toString().replace('\\', '/'), f));
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }

    ByteArrayOutputStream bytes = new ByteArrayOutputStream();
    try (ZipOutputStream zip = new ZipOutputStream(bytes)) {
      for (Map.Entry<String, Path> entry : entries.entrySet()) {
        ZipEntry zipEntry = new ZipEntry(entry.getKey());
        zipEntry.setTime(ENTRY_TIME);
        zip.putNextEntry(zipEntry);
        zip.write(Files.readAllBytes(entry.getValue()));
        zip.closeEntry();
      }
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }
    return bytes.toByteArray();
  }

  // Maven repositories that accept plain HTTP PUT expect .md5 and .sha1 next to each file.
  private static void write(FileManifest out, String name, byte[] content) {
    out.writeFile(name, new ByteArrayInputStream(content));
    out.writeFile(name + ".md5", digest("MD5", content));
    out.writeFile(name + ".sha1", digest("SHA-1", content));
  }

  private static String digest(String algorithm, byte[] content) {
    try {
      return HexFormat.of().formatHex(MessageDigest.getInstance(algorithm).digest(content));
    } catch (NoSuchAlgorithmException e) {
      throw new IllegalStateException(e);
    }
  }

  private static NavigableSet<String> explicitDependencies(ArrayNode node) {
    NavigableSet<String> coordinates = new TreeSet<>();
    for (StringNode value : node.getElementsAs(StringNode.class)) {
      String coordinate = value.getValue();
      String[] parts = coordinate.split(":", -1);
      if (parts.length != 3 || Stream.of(parts).anyMatch(String::isBlank)) {
        throw new SmithyBuildException(
            "contracts-jar: dependency '"
                + coordinate
                + "' is not a groupId:artifactId:version coordinate");
      }
      coordinates.add(coordinate);
    }
    return coordinates;
  }

  private static String requiredString(ObjectNode settings, String name) {
    return settings
        .getStringMember(name)
        .map(StringNode::getValue)
        .filter(v -> !v.isBlank())
        .orElseThrow(
            () -> new SmithyBuildException("contracts-jar: `" + name + "` setting is required"));
  }

  private static void deleteRecursively(Path root) {
    try (Stream<Path> files = Files.walk(root)) {
      List<Path> paths = new ArrayList<>(files.toList());
      paths.sort(Comparator.reverseOrder());
      for (Path path : paths) {
        Files.deleteIfExists(path);
      }
    } catch (IOException ignored) {
      // Best effort: a leftover temp directory is harmless.
    }
  }
}

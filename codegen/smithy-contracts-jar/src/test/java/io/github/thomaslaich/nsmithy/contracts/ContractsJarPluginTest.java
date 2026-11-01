package io.github.thomaslaich.nsmithy.contracts;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.net.URISyntaxException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class ContractsJarPluginTest {

  private static final ObjectNode COORDINATES =
      Node.objectNodeBuilder()
          .withMember("groupId", "io.github.acme")
          .withMember("artifactId", "hello-contracts")
          .withMember("version", "1.2.3")
          .build();

  @Test
  void packagesSourcesInDiscoveryLayout() throws Exception {
    MockManifest manifest = run(COORDINATES);

    Map<String, String> jar =
        readJar(manifest.getFileBytes("hello-contracts-1.2.3.jar").orElseThrow());
    assertEquals("hello.smithy\n", jar.get("META-INF/smithy/manifest"));
    assertTrue(jar.get("META-INF/smithy/hello.smithy").contains("service HelloService"));
    assertEquals(2, jar.size());
    assertTrue(manifest.hasFile("hello-contracts-1.2.3.jar.sha1"));
    assertTrue(manifest.hasFile("hello-contracts-1.2.3.pom.md5"));
  }

  @Test
  void jarIsReproducible() throws Exception {
    assertEquals(manifestJar(run(COORDINATES)), manifestJar(run(COORDINATES)));
  }

  @Test
  void pomDeclaresOnlyTheTraitPackagesTheModelUses() throws Exception {
    String pom = run(COORDINATES).getFileString("hello-contracts-1.2.3.pom").orElseThrow();

    assertTrue(pom.contains("<groupId>io.github.acme</groupId>"), pom);
    assertTrue(pom.contains("<artifactId>smithy-aws-traits</artifactId>"), pom);
    // smithy-aws-traits pulls in other model JARs transitively; only the direct one is declared.
    assertEquals(1, pom.split("<dependency>", -1).length - 1, pom);
  }

  @Test
  void explicitDependenciesWin() throws Exception {
    ObjectNode settings =
        COORDINATES.withMember("dependencies", Node.fromStrings("com.example:custom-traits:9.9.9"));
    String pom = run(settings).getFileString("hello-contracts-1.2.3.pom").orElseThrow();

    assertTrue(pom.contains("<artifactId>custom-traits</artifactId>"), pom);
    assertTrue(!pom.contains("smithy-aws-traits"), pom);
  }

  @Test
  void rejectsMalformedDependency() {
    ObjectNode settings =
        COORDINATES.withMember("dependencies", Node.fromStrings("not-a-coordinate"));
    assertThrows(SmithyBuildException.class, () -> run(settings));
  }

  @Test
  void requiresCoordinates() {
    assertThrows(SmithyBuildException.class, () -> run(COORDINATES.withoutMember("version")));
  }

  @Test
  void rejectsReferencesToLooseNonSourceFiles(@TempDir Path dir) throws IOException {
    Path shared = dir.resolve("shared.smithy");
    Files.writeString(shared, "$version: \"2\"\nnamespace example.shared\nstring Name\n");
    Path own = dir.resolve("own.smithy");
    Files.writeString(
        own,
        "$version: \"2\"\nnamespace example.own\nstructure Person { name: example.shared#Name }\n");
    Model model = Model.assembler().addImport(shared).addImport(own).assemble().unwrap();

    PluginContext context =
        PluginContext.builder()
            .model(model)
            .originalModel(model)
            .sources(List.of(own))
            .settings(COORDINATES)
            .fileManifest(new MockManifest())
            .build();

    SmithyBuildException error =
        assertThrows(SmithyBuildException.class, () -> new ContractsJarPlugin().execute(context));
    assertTrue(error.getMessage().contains("shared.smithy"), error.getMessage());
  }

  @Test
  void parsesMavenLayout() {
    Path root = Path.of("/home/me/.m2/repository");
    Path jar =
        root.resolve(
            "software/amazon/smithy/smithy-aws-traits/1.73.0/smithy-aws-traits-1.73.0.jar");
    assertEquals(
        Optional.of("software.amazon.smithy:smithy-aws-traits:1.73.0"),
        DependencyResolver.coordinateOf(jar, List.of(root)));
  }

  @Test
  void parsesGradleLayout() {
    Path jar =
        Path.of(
            "/home/me/.gradle/caches/modules-2/files-2.1/com.disneystreaming.alloy/alloy-core/0.3.39/"
                + "abc123/alloy-core-0.3.39.jar");
    assertEquals(
        Optional.of("com.disneystreaming.alloy:alloy-core:0.3.39"),
        DependencyResolver.coordinateOf(jar, List.of()));
  }

  @Test
  void doesNotGuessOutsideKnownCaches() {
    Path jar =
        Path.of(
            "/opt/libs/software/amazon/smithy/smithy-aws-traits/1.73.0/smithy-aws-traits-1.73.0.jar");
    assertEquals(
        Optional.empty(),
        DependencyResolver.coordinateOf(jar, List.of(Path.of("/home/me/.m2/repository"))));
  }

  private static MockManifest run(ObjectNode settings) throws URISyntaxException {
    Path source = Path.of(ContractsJarPluginTest.class.getResource("hello.smithy").toURI());
    Model model =
        Model.assembler(ContractsJarPluginTest.class.getClassLoader())
            .discoverModels(ContractsJarPluginTest.class.getClassLoader())
            .addImport(source)
            .assemble()
            .unwrap();
    MockManifest manifest = new MockManifest();
    new ContractsJarPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .originalModel(model)
                .sources(List.of(source))
                .settings(settings)
                .fileManifest(manifest)
                .build());
    return manifest;
  }

  private static String manifestJar(MockManifest manifest) {
    return new String(
        manifest.getFileBytes("hello-contracts-1.2.3.jar").orElseThrow(),
        StandardCharsets.ISO_8859_1);
  }

  private static Map<String, String> readJar(byte[] bytes) throws IOException {
    Map<String, String> entries = new TreeMap<>();
    try (ZipInputStream zip = new ZipInputStream(new ByteArrayInputStream(bytes))) {
      for (ZipEntry entry = zip.getNextEntry(); entry != null; entry = zip.getNextEntry()) {
        entries.put(entry.getName(), new String(zip.readAllBytes(), StandardCharsets.UTF_8));
      }
    }
    return entries;
  }
}

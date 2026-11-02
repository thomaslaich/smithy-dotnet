package io.github.thomaslaich.nsmithy.contracts;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.net.URISyntaxException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.TimeZone;
import java.util.TreeMap;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;
import org.junit.jupiter.api.Test;
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
          .withMember(
              "dependencies", Node.fromStrings("software.amazon.smithy:smithy-aws-traits:1.74.1"))
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
  void jarDoesNotDependOnTimeZone() throws Exception {
    TimeZone original = TimeZone.getDefault();
    try {
      TimeZone.setDefault(TimeZone.getTimeZone("UTC"));
      String utc = manifestJar(run(COORDINATES));
      TimeZone.setDefault(TimeZone.getTimeZone("Pacific/Auckland"));
      assertEquals(utc, manifestJar(run(COORDINATES)));
    } finally {
      TimeZone.setDefault(original);
    }
  }

  @Test
  void pomDeclaresExplicitDependencies() throws Exception {
    String pom = run(COORDINATES).getFileString("hello-contracts-1.2.3.pom").orElseThrow();

    assertTrue(pom.contains("<groupId>io.github.acme</groupId>"), pom);
    assertTrue(pom.contains("<artifactId>smithy-aws-traits</artifactId>"), pom);
    assertEquals(1, pom.split("<dependency>", -1).length - 1, pom);
  }

  @Test
  void rendersPom() throws Exception {
    ObjectNode settings =
        COORDINATES.withMember(
            "dependencies",
            Node.fromStrings(
                "software.amazon.smithy:smithy-aws-traits:1.74.1", "com.example:a&b:1.0"));
    String pom = run(settings).getFileString("hello-contracts-1.2.3.pom").orElseThrow();

    assertEquals(
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0" \
        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" \
        xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 \
        https://maven.apache.org/xsd/maven-4.0.0.xsd">
          <modelVersion>4.0.0</modelVersion>
          <groupId>io.github.acme</groupId>
          <artifactId>hello-contracts</artifactId>
          <version>1.2.3</version>
          <packaging>jar</packaging>
          <dependencies>
            <dependency>
              <groupId>com.example</groupId>
              <artifactId>a&amp;b</artifactId>
              <version>1.0</version>
            </dependency>
            <dependency>
              <groupId>software.amazon.smithy</groupId>
              <artifactId>smithy-aws-traits</artifactId>
              <version>1.74.1</version>
            </dependency>
          </dependencies>
        </project>
        """,
        pom);
  }

  @Test
  void rendersPomWithoutDependencies() throws Exception {
    String pom =
        run(COORDINATES.withMember("dependencies", Node.fromStrings()))
            .getFileString("hello-contracts-1.2.3.pom")
            .orElseThrow();

    assertTrue(pom.endsWith("  <packaging>jar</packaging>\n</project>\n"), pom);
  }

  @Test
  void doesNotInferAdditionalDependencies() throws Exception {
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
  void requiresDependencies() {
    SmithyBuildException error =
        assertThrows(
            SmithyBuildException.class, () -> run(COORDINATES.withoutMember("dependencies")));
    assertTrue(error.getMessage().contains("`dependencies` setting is required"));
  }

  @Test
  void acceptsEmptyDependencies() throws Exception {
    String pom =
        run(COORDINATES.withMember("dependencies", Node.fromStrings()))
            .getFileString("hello-contracts-1.2.3.pom")
            .orElseThrow();
    assertTrue(!pom.contains("<dependency>"), pom);
  }

  @Test
  void rejectsNonArrayDependencies() {
    assertThrows(
        RuntimeException.class,
        () -> run(COORDINATES.withMember("dependencies", "com.example:traits:1.0")));
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

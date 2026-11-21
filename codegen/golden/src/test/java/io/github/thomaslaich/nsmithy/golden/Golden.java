package io.github.thomaslaich.nsmithy.golden;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.loader.ModelAssembler;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;

/**
 * Runs a generator over every fixture under {@code codegen/golden} and compares its output with the
 * files checked in at {@code <fixture>/<language>/<run>/}. With {@code -PupdateGolden}, rewrites
 * them instead.
 *
 * <p>A fixture is a {@code model} directory, whose files every run loads, and a {@code
 * fixture.json}:
 *
 * <pre>{@code
 * {
 *   "languages": ["csharp", "fsharp"],      // optional; both by default
 *   "runs": [
 *     { "name": "orders", "settings": { "ownedSources": ["orders.smithy"] } },
 *     { "name": "api",
 *       "settings": { "service": "acme.shop#Shop", "ownedSources": ["api.smithy"] },
 *       "provided": ["orders"] }             // earlier runs whose shape manifests this one uses
 *   ]
 * }
 * }</pre>
 *
 * <p>{@code ownedSources} name files of the model directory. A run's settings are the plugin's
 * settings; {@code csharp} and {@code fsharp} members add settings for one language only.
 *
 * <p>The output is formatted the way the plugins format it, with the formatter pinned in the
 * repository's {@code dotnet-tools.json}, so the checked-in files are what users get.
 */
public final class Golden {

  /** Runs one generator: the model, its settings, and the manifest to write to. */
  @FunctionalInterface
  public interface Generator {
    void generate(Model model, ObjectNode settings, FileManifest manifest);
  }

  /** The {@code dotnet} tool command that formats the generated files in a directory, in place. */
  @FunctionalInterface
  public interface Formatter {
    List<String> command(Path dir);
  }

  private static final String MANIFEST = "nsmithy-shapes.json";
  private static final Path ROOT = Path.of(System.getProperty("nsmithy.golden.dir"));
  private static final boolean UPDATE = Boolean.getBoolean("nsmithy.golden.update");
  // Holds dotnet-tools.json; `dotnet tool run` finds the manifest from its working directory.
  private static final Path REPOSITORY = ROOT.getParent().getParent();

  private Golden() {}

  /** One test per fixture that includes {@code language}. */
  public static Stream<DynamicTest> tests(
      String language, Generator generator, Formatter formatter) {
    return fixtures().stream()
        .filter(fixture -> languages(fixture).contains(language))
        .map(fixture ->
            DynamicTest.dynamicTest(
                fixture.getFileName().toString(),
                () -> run(fixture, language, generator, formatter)));
  }

  private static List<Path> fixtures() {
    try (Stream<Path> entries = Files.list(ROOT)) {
      return entries
          .filter(dir -> Files.isRegularFile(dir.resolve("fixture.json")))
          .sorted()
          .toList();
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }
  }

  private static List<String> languages(Path fixture) {
    return config(fixture)
        .getArrayMember("languages")
        .map(array -> array.getElementsAs(StringNode::getValue))
        .orElse(List.of("csharp", "fsharp"));
  }

  private static ObjectNode config(Path fixture) {
    return Node.parse(read(fixture.resolve("fixture.json"))).expectObjectNode();
  }

  private static void run(Path fixture, String language, Generator generator, Formatter formatter)
      throws IOException, InterruptedException {
    Path models = fixture.resolve("model");
    ModelAssembler assembler = Model.assembler().discoverModels();
    try (Stream<Path> files = Files.list(models)) {
      files
          .filter(file -> file.toString().endsWith(".smithy"))
          .sorted()
          .forEach(assembler::addImport);
    }
    Model model = assembler.assemble().unwrap();

    // The output of every run, by path below the language directory.
    Map<String, String> actual = new TreeMap<>();
    Map<String, Path> manifests = new HashMap<>();
    Path scratch = Files.createTempDirectory("nsmithy-golden");
    for (ObjectNode run :
        config(fixture).expectArrayMember("runs").getElementsAs(ObjectNode.class)) {
      String name = run.expectStringMember("name").getValue();
      ObjectNode settings = settings(run, language, models, manifests);
      MockManifest manifest = new MockManifest();
      generator.generate(model, settings, manifest);
      for (Path file : manifest.getFiles()) {
        String relative = manifest.getBaseDir().relativize(file).toString().replace('\\', '/');
        actual.put(name + "/" + relative, normalize(manifest.expectFileString(file), scratch));
      }
      if (manifest.hasFile(MANIFEST)) {
        Path written = scratch.resolve(name + "-" + MANIFEST);
        Files.writeString(written, manifest.expectFileString(MANIFEST));
        manifests.put(name, written);
      }
    }

    actual = format(actual, formatter, scratch);

    Path expectedDir = fixture.resolve(language);
    if (UPDATE) {
      write(expectedDir, actual);
      return;
    }
    Map<String, String> expected = readAll(expectedDir);
    List<String> problems = new ArrayList<>();
    TreeSet<String> paths = new TreeSet<>(expected.keySet());
    paths.addAll(actual.keySet());
    for (String path : paths) {
      if (!expected.containsKey(path)) {
        problems.add("new file " + path);
      } else if (!actual.containsKey(path)) {
        problems.add("file no longer generated: " + path);
      } else if (!expected.get(path).equals(actual.get(path))) {
        problems.add(
            "changed " + path + "\n" + firstDifference(expected.get(path), actual.get(path)));
      }
    }
    if (!problems.isEmpty()) {
      throw new AssertionError(
          "Generated "
              + language
              + " for fixture "
              + fixture.getFileName()
              + " differs from "
              + REPOSITORY.relativize(expectedDir)
              + ":\n  "
              + String.join("\n  ", problems)
              + "\nIf the change is intended, run `just golden` and commit the result.");
    }
  }

  /** The run's settings for {@code language}, with model files and provided manifests resolved. */
  private static ObjectNode settings(
      ObjectNode run, String language, Path models, Map<String, Path> manifests) {
    ObjectNode settings = run.getObjectMember("settings").orElse(Node.objectNode());
    settings = settings.merge(run.getObjectMember(language).orElse(Node.objectNode()));
    if (settings.getArrayMember("ownedSources").isPresent()) {
      settings =
          settings.withMember(
              "ownedSources",
              ArrayNode.fromStrings(
                  settings
                      .expectArrayMember("ownedSources")
                      .getElementsAs(StringNode::getValue)
                      .stream()
                      .map(source -> models.resolve(source).toString())
                      .toList()));
    }
    List<String> provided =
        run.getArrayMember("provided")
            .map(array -> array.getElementsAs(StringNode::getValue))
            .orElse(List.of());
    if (!provided.isEmpty()) {
      settings =
          settings.withMember(
              "providedShapes",
              ArrayNode.fromStrings(
                  provided.stream()
                      .map(name -> {
                        Path manifest = manifests.get(name);
                        if (manifest == null) {
                          throw new IllegalArgumentException(
                              "Run " + name + " is not an earlier run with a shape manifest");
                        }
                        return manifest.toString();
                      })
                      .toList()));
    }
    return settings;
  }

  /** Leaves out what differs between machines and versions: temporary paths and the version. */
  private static String normalize(String content, Path scratch) {
    return content
        .replace(scratch.toString(), "<scratch>")
        .replace(ROOT.toString(), "<golden>")
        .replaceAll(
            "\"generatorVersion\"\\s*:\\s*\"[^\"]*\"", "\"generatorVersion\": \"<version>\"");
  }

  /**
   * Formats the files outside the repository, so no {@code .editorconfig} or ignore file of it
   * applies, and after normalizing, so machine-specific paths cannot change line breaks.
   */
  private static Map<String, String> format(
      Map<String, String> files, Formatter formatter, Path scratch)
      throws IOException, InterruptedException {
    Path dir = scratch.resolve("format");
    write(dir, files);
    List<String> command = new ArrayList<>();
    command.add("dotnet");
    command.addAll(formatter.command(dir));
    Process process =
        new ProcessBuilder(command)
            .directory(REPOSITORY.toFile())
            .redirectErrorStream(true)
            .start();
    String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
    if (process.waitFor() != 0) {
      throw new AssertionError(
          String.join(" ", command)
              + " failed. Run `dotnet tool restore` in the repository first.\n"
              + output);
    }
    return readAll(dir);
  }

  private static Map<String, String> readAll(Path dir) throws IOException {
    Map<String, String> files = new TreeMap<>();
    if (!Files.isDirectory(dir)) {
      return files;
    }
    try (Stream<Path> paths = Files.walk(dir)) {
      for (Path file : paths.filter(Files::isRegularFile).toList()) {
        files.put(dir.relativize(file).toString().replace('\\', '/'), read(file));
      }
    }
    return files;
  }

  private static void write(Path dir, Map<String, String> files) throws IOException {
    if (Files.isDirectory(dir)) {
      try (Stream<Path> paths = Files.walk(dir)) {
        for (Path path : paths.sorted((a, b) -> b.compareTo(a)).toList()) {
          Files.delete(path);
        }
      }
    }
    for (Map.Entry<String, String> file : files.entrySet()) {
      Path path = dir.resolve(file.getKey());
      Files.createDirectories(path.getParent());
      Files.writeString(path, file.getValue(), StandardCharsets.UTF_8);
    }
  }

  private static String read(Path file) {
    try {
      return Files.readString(file, StandardCharsets.UTF_8);
    } catch (IOException e) {
      throw new UncheckedIOException(e);
    }
  }

  /** The first line that differs, with its line number. */
  private static String firstDifference(String expected, String actual) {
    String[] expectedLines = expected.split("\n", -1);
    String[] actualLines = actual.split("\n", -1);
    for (int i = 0; i < Math.max(expectedLines.length, actualLines.length); i++) {
      String want = i < expectedLines.length ? expectedLines[i] : "<end of file>";
      String got = i < actualLines.length ? actualLines[i] : "<end of file>";
      if (!want.equals(got)) {
        return "    line " + (i + 1) + "\n    expected: " + want + "\n    actual:   " + got;
      }
    }
    return "    (no line differs)";
  }
}

package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeClosures;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.io.IOException;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Optional;
import java.util.ServiceLoader;
import java.util.Set;
import java.util.TreeSet;
import java.util.logging.Logger;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpCodegenPlugin implements SmithyBuildPlugin {

  private static final Logger LOGGER = Logger.getLogger(CSharpCodegenPlugin.class.getName());

  @Override
  public String getName() {
    return "csharp-codegen";
  }

  @Override
  public void execute(PluginContext context) {
    List<CSharpIntegration> integrations = new ArrayList<>();
    ServiceLoader.load(CSharpIntegration.class, CodegenDirector.class.getClassLoader())
        .forEach(integrations::add);

    generate(
        context.getModel(),
        CSharpSettings.fromNode(context.getSettings()),
        context.getFileManifest(),
        integrations);

    formatGeneratedCode(context.getFileManifest().getBaseDir());
  }

  /** Runs directed codegen for {@code settings} with the given integrations. */
  static void generate(
      Model model,
      CSharpSettings settings,
      FileManifest manifest,
      List<CSharpIntegration> integrations) {
    CodegenDirector<CSharpWriter, CSharpIntegration, GenerationContext, CSharpSettings> runner =
        new CodegenDirector<>();

    runner.settings(settings);
    runner.directedCodegen(new DirectedCSharpCodegen());
    runner.fileManifest(manifest);
    runner.model(model);
    runner.integrationClass(CSharpIntegration.class);
    runner.integrationFinder(() -> integrations);
    runner.performDefaultCodegenTransforms();
    runner.createDedicatedInputsAndOutputs();

    Optional<ServiceShape> service =
        settings.service().map(id -> model.expectShape(id, ServiceShape.class));
    Set<ShapeId> additionalShapes =
        service
            .map(shape -> additionalShapes(model, settings, shape, integrations))
            .orElseGet(TreeSet::new);
    ShapeClosures.configure(runner, model, service, settings.closure(), additionalShapes);

    runner.run();
  }

  private static Set<ShapeId> additionalShapes(
      Model model,
      CSharpSettings settings,
      ServiceShape service,
      List<CSharpIntegration> integrations) {
    Set<ShapeId> shapes = new TreeSet<>();
    for (CSharpIntegration integration : integrations) {
      for (ShapeId id : integration.additionalShapes(model, settings, service)) {
        if (model.getShape(id).isEmpty()) {
          throw new CodegenException(
              "C# integration "
                  + integration.name()
                  + " adds shape "
                  + id
                  + " to "
                  + service.getId()
                  + ", but the model has no such shape.");
        }
        shapes.add(id);
      }
    }
    return shapes;
  }

  private static final String NULL_DEVICE =
      System.getProperty("os.name", "").toLowerCase(Locale.ROOT).contains("win")
          ? "NUL"
          : "/dev/null";

  private static void formatGeneratedCode(Path outputDir) {
    String dir = outputDir.toAbsolutePath().toString();
    for (List<String> cmd :
        List.of(
            List.of(
                "csharpier", "format", "--include-generated", "--ignore-path", NULL_DEVICE, dir),
            List.of(
                "dotnet",
                "csharpier",
                "format",
                "--include-generated",
                "--ignore-path",
                NULL_DEVICE,
                dir))) {
      try {
        int exit = new ProcessBuilder(cmd).redirectErrorStream(true).inheritIO().start().waitFor();
        if (exit == 0) return;
        LOGGER.fine(cmd + " exited with code " + exit + " — trying next");
      } catch (IOException | InterruptedException e) {
        LOGGER.fine(cmd + " not available: " + e.getMessage());
        if (e instanceof InterruptedException) Thread.currentThread().interrupt();
      }
    }
    LOGGER.fine("csharpier not found — generated files left unformatted");
  }
}

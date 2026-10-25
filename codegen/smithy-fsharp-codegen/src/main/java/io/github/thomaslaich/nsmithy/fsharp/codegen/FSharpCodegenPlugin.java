package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.GeneratedCodeFormatter;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeClosures;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.ServiceLoader;
import java.util.TreeSet;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * Generates F# types, schemas, and the service's client and server: {@code Types.g.fs}, {@code
 * Schemas.g.fs}, {@code Client.g.fs}, then {@code Server.g.fs}, which a project compiles in that
 * order ahead of its own files.
 */
@SmithyUnstableApi
public final class FSharpCodegenPlugin implements SmithyBuildPlugin {

  @Override
  public String getName() {
    return "fsharp-codegen";
  }

  @Override
  public void execute(PluginContext context) {
    List<FSharpIntegration> integrations = new ArrayList<>();
    ServiceLoader.load(FSharpIntegration.class, CodegenDirector.class.getClassLoader())
        .forEach(integrations::add);
    generate(
        context.getModel(),
        FSharpSettings.fromNode(context.getSettings()),
        context.getFileManifest(),
        integrations);

    formatGeneratedCode(context.getFileManifest());
  }

  static void generate(
      Model model,
      FSharpSettings settings,
      FileManifest manifest,
      List<FSharpIntegration> integrations) {
    CodegenDirector<FSharpWriter, FSharpIntegration, FSharpContext, FSharpSettings> runner =
        new CodegenDirector<>();
    runner.settings(settings);
    runner.directedCodegen(new DirectedFSharpCodegen());
    runner.fileManifest(manifest);
    runner.model(model);
    runner.integrationClass(FSharpIntegration.class);
    runner.integrationFinder(() -> integrations);
    runner.performDefaultCodegenTransforms();
    runner.createDedicatedInputsAndOutputs();
    Optional<ServiceShape> service =
        settings.service().map(id -> model.expectShape(id, ServiceShape.class));
    ShapeClosures.configure(runner, model, service, settings.closure(), new TreeSet<>());
    runner.run();
  }

  // Fantomas skips files under obj/ when handed a directory, so it gets each file.
  private static void formatGeneratedCode(FileManifest manifest) {
    List<String> files =
        manifest.getFiles().stream()
            .filter(file -> file.toString().endsWith(".fs"))
            .map(file -> file.toAbsolutePath().toString())
            .sorted()
            .toList();
    if (!files.isEmpty()) {
      GeneratedCodeFormatter.format("fantomas", files);
    }
  }
}

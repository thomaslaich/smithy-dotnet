package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.GeneratedCodeFormatter;
import io.github.thomaslaich.nsmithy.codegen.core.OwnedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeClosures;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.ServiceLoader;
import java.util.Set;
import java.util.TreeSet;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.transform.ModelTransformer;
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
      Model sourceModel,
      FSharpSettings settings,
      FileManifest manifest,
      List<FSharpIntegration> integrations) {
    // Not performDefaultCodegenTransforms: it also copies a service's errors onto its operations,
    // but operation schemas are service-agnostic. The service schema carries those errors.
    Model model = ModelTransformer.create().flattenAndRemoveMixins(sourceModel);
    CodegenDirector<FSharpWriter, FSharpIntegration, FSharpContext, FSharpSettings> runner =
        new CodegenDirector<>();
    runner.settings(settings);
    runner.directedCodegen(new DirectedFSharpCodegen());
    runner.fileManifest(manifest);
    runner.model(model);
    runner.integrationClass(FSharpIntegration.class);
    runner.integrationFinder(() -> integrations);
    runner.createDedicatedInputsAndOutputs();
    Optional<ServiceShape> service =
        settings.service().map(id -> model.expectShape(id, ServiceShape.class));
    Set<ShapeId> owned = OwnedShapes.of(model, settings.ownedSources());
    settings.providedShapes().check(model, service, owned, settings.generateServer());
    if (settings.generatesOwnedShapes()) {
      if (!ShapeClosures.configureOwned(runner, service, owned, new TreeSet<>())) {
        // Nothing to generate; the empty manifest still tells consumers this project owns nothing.
        new ShapeManifest(ShapeManifest.FSHARP_GENERATOR)
            .write(manifest, settings.providedShapes());
        return;
      }
    } else {
      ShapeClosures.configure(runner, model, service, settings.closure(), new TreeSet<>());
    }
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

package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.GeneratedCodeFormatter;
import io.github.thomaslaich.nsmithy.codegen.core.OwnedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeClosures;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import io.github.thomaslaich.nsmithy.codegen.core.ValidationErrors;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import java.util.ServiceLoader;
import java.util.Set;
import java.util.TreeSet;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpCodegenPlugin implements SmithyBuildPlugin {

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
      Model sourceModel,
      CSharpSettings settings,
      FileManifest manifest,
      List<CSharpIntegration> integrations) {
    // Not performDefaultCodegenTransforms: it also copies a service's errors onto its operations,
    // but operation schemas are service-agnostic. The service schema carries those errors.
    Model model = ModelTransformer.create().flattenAndRemoveMixins(sourceModel);
    if (settings.generateServer()) {
      // The server answers a constraint violation with ValidationException; the model must say so.
      settings
          .service()
          .ifPresent(id ->
              ValidationErrors.requireDeclared(model, model.expectShape(id, ServiceShape.class)));
    }
    CodegenDirector<CSharpWriter, CSharpIntegration, GenerationContext, CSharpSettings> runner =
        new CodegenDirector<>();

    runner.settings(settings);
    runner.directedCodegen(new DirectedCSharpCodegen());
    runner.fileManifest(manifest);
    runner.model(model);
    runner.integrationClass(CSharpIntegration.class);
    runner.integrationFinder(() -> integrations);
    runner.createDedicatedInputsAndOutputs();

    Optional<ServiceShape> service =
        settings.service().map(id -> model.expectShape(id, ServiceShape.class));
    Set<ShapeId> additionalShapes =
        service
            .map(shape -> additionalShapes(model, settings, shape, integrations))
            .orElseGet(TreeSet::new);
    Set<ShapeId> owned = OwnedShapes.of(model, settings.ownedSources());
    settings.providedShapes().check(model, service, owned, settings.generateServer());
    if (settings.generatesOwnedShapes()) {
      if (!ShapeClosures.configureOwned(runner, service, owned, additionalShapes)) {
        // Nothing to generate; the empty manifest still tells consumers this project owns nothing.
        new ShapeManifest(ShapeManifest.CSHARP_GENERATOR)
            .write(manifest, settings.providedShapes());
        return;
      }
    } else {
      ShapeClosures.configure(runner, model, service, settings.closure(), additionalShapes);
    }

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

  private static void formatGeneratedCode(Path outputDir) {
    GeneratedCodeFormatter.format(
        "csharpier",
        List.of(
            "format",
            "--include-generated",
            "--ignore-path",
            GeneratedCodeFormatter.NULL_DEVICE,
            outputDir.toAbsolutePath().toString()));
  }
}

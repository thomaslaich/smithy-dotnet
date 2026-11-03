package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.GeneratedCodeFormatter;
import io.github.thomaslaich.nsmithy.codegen.core.OwnedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeClosures;
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
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
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
    Set<ShapeId> owned = OwnedShapes.of(model, settings.ownedSources());
    checkProvidedShapes(settings, service, owned, model);
    if (settings.generatesOwnedShapes()) {
      if (!ShapeClosures.configureOwned(runner, service, owned, additionalShapes)) {
        // Nothing to generate; the empty manifest still tells consumers this project owns nothing.
        new ShapeManifest().write(manifest);
        return;
      }
    } else {
      ShapeClosures.configure(runner, model, service, settings.closure(), additionalShapes);
    }

    runner.run();
  }

  /**
   * Fails when the provided shapes cannot be used as they are: a shape this project defines itself,
   * a service rename that would give a provided shape another name, or a provided operation without
   * the handler interface the service's server needs.
   */
  private static void checkProvidedShapes(
      CSharpSettings settings, Optional<ServiceShape> service, Set<ShapeId> owned, Model model) {
    ProvidedShapes provided = settings.providedShapes();
    for (ShapeId id : owned) {
      if (provided.contains(id)) {
        throw new CodegenException(
            "Shape "
                + id
                + " is defined in this project's own model files, but the shape manifest "
                + provided.manifest(id).orElseThrow()
                + " provides it too. A project's own manifest must not be passed to it.");
      }
    }
    if (service.isEmpty()) {
      return;
    }
    for (ShapeId id : service.get().getRename().keySet()) {
      if (provided.contains(id)) {
        throw new CodegenException(
            "Service "
                + service.get().getId()
                + " renames "
                + id
                + ", but the shape manifest "
                + provided.manifest(id).orElseThrow()
                + " provides that shape under the name its own project gave it. Rename the shape"
                + " in its own model instead.");
      }
    }
    if (settings.generateServer()) {
      for (OperationShape op : TopDownIndex.of(model).getContainedOperations(service.get())) {
        if (provided.contains(op.getId()) && provided.handler(op.getId()).isEmpty()) {
          throw new CodegenException(
              "Operation "
                  + op.getId()
                  + " of "
                  + service.get().getId()
                  + " is provided by the shape manifest "
                  + provided.manifest(op.getId()).orElseThrow()
                  + " without a handler interface. Generate the server in the project that owns"
                  + " the operation (SmithyGenerateServer), or turn the server off here.");
        }
      }
    }
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

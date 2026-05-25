package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.io.IOException;
import java.nio.file.Path;
import java.util.List;
import java.util.logging.Logger;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpClientCodegenPlugin implements SmithyBuildPlugin {

  private static final Logger LOGGER =
      Logger.getLogger(CSharpClientCodegenPlugin.class.getName());

  @Override
  public String getName() {
    return "csharp-codegen";
  }

  @Override
  public void execute(PluginContext context) {
    CodegenDirector<CSharpWriter, CSharpIntegration, GenerationContext, CSharpSettings> runner =
        new CodegenDirector<>();

    CSharpSettings settings = CSharpSettings.fromNode(context.getSettings());

    runner.settings(settings);
    runner.directedCodegen(new DirectedCSharpClientCodegen());
    runner.fileManifest(context.getFileManifest());
    runner.service(settings.service());
    runner.model(context.getModel());
    runner.integrationClass(CSharpIntegration.class);
    runner.performDefaultCodegenTransforms();
    runner.createDedicatedInputsAndOutputs();
    runner.run();

    formatGeneratedCode(context.getFileManifest().getBaseDir());
  }

  private static final String NULL_DEVICE =
      System.getProperty("os.name", "").toLowerCase().contains("win") ? "NUL" : "/dev/null";

  private static void formatGeneratedCode(Path outputDir) {
    String dir = outputDir.toAbsolutePath().toString();
    for (List<String> cmd :
        List.of(
            List.of("csharpier", "format", "--include-generated", "--ignore-path", NULL_DEVICE, dir),
            List.of("dotnet", "csharpier", "format", "--include-generated", "--ignore-path", NULL_DEVICE, dir))) {
      try {
        int exit =
            new ProcessBuilder(cmd).redirectErrorStream(true).inheritIO().start().waitFor();
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

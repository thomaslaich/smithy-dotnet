/*
 * SmithyBuildPlugin entry point. This is intentionally tiny - the heavy
 * lifting lives in DirectedCSharpClientCodegen. Mirrors PythonClientCodegenPlugin
 * and TypeScriptClientCodegenPlugin: parse settings, build a CodegenDirector,
 * and call run().
 */
package io.github.thomaslaich.smithy.csharp.codegen;

import io.github.thomaslaich.smithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.build.SmithyBuildPlugin;
import software.amazon.smithy.codegen.core.directed.CodegenDirector;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpClientCodegenPlugin implements SmithyBuildPlugin {

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
  }
}

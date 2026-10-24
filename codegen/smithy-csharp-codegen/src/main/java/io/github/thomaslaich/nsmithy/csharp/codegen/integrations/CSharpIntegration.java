/*
 * SmithyIntegration extension point for the C# generator. Third parties can
 * implement this on the classpath and CodegenDirector will discover them via
 * the Java SPI. Mirrors PythonIntegration / TypeScriptIntegration.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.integrations;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SmithyIntegration;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public interface CSharpIntegration
    extends SmithyIntegration<CSharpSettings, CSharpWriter, GenerationContext> {

  /**
   * Replaces the generated client and server surface of {@code service}, for a protocol this
   * integration supplies. Shapes, schemas, and operations are still generated as usual. At most one
   * integration may provide a service generator for a service.
   */
  default Optional<CSharpServiceGenerator> serviceGenerator(
      GenerationContext context, ServiceShape service) {
    return Optional.empty();
  }
}

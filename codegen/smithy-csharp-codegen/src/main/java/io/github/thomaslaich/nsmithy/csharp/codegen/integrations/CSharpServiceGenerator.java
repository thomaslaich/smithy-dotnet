package io.github.thomaslaich.nsmithy.csharp.codegen.integrations;

import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** Generates the service-level surface for a protocol supplied by a C# integration. */
@FunctionalInterface
@SmithyUnstableApi
public interface CSharpServiceGenerator {
  void generate(GenerationContext context, ServiceShape service);
}

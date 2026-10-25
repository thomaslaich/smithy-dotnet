/*
 * Amazon Glacier's client customization, which its model does not describe.
 */
package io.github.thomaslaich.nsmithy.csharp.aws.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.sections.ClientConfigDefaultsSection;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.CodeInterceptor;
import software.amazon.smithy.utils.CodeSection;
import software.amazon.smithy.utils.SmithyInternalApi;

/** Adds {@code GlacierInterceptor} to the Glacier client's default configuration. */
@SmithyInternalApi
public final class GlacierIntegration implements CSharpIntegration {

  private static final ShapeId GLACIER = ShapeId.from("com.amazonaws.glacier#Glacier");

  @Override
  public String name() {
    return "aws-glacier";
  }

  @Override
  public List<? extends CodeInterceptor<? extends CodeSection, CSharpWriter>> interceptors(
      GenerationContext context) {
    return List.of(
        CodeInterceptor.appender(
            ClientConfigDefaultsSection.class,
            (writer, section) -> {
              if (section.service().getId().equals(GLACIER)) {
                writer.write("Interceptors.Add(new $T());", AwsRuntimeTypes.GLACIER_INTERCEPTOR);
              }
            }));
  }
}

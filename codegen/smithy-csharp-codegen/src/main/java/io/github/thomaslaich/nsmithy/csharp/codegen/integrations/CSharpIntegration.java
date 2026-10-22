/*
 * SmithyIntegration extension point for the C# generator. Third parties can
 * implement this on the classpath and CodegenDirector will discover them via
 * the Java SPI. Mirrors PythonIntegration / TypeScriptIntegration.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.integrations;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointBuiltIn;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointDefaultAuthScheme;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointFunction;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.codegen.core.SmithyIntegration;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public interface CSharpIntegration
    extends SmithyIntegration<CSharpSettings, CSharpWriter, GenerationContext> {

  /** Endpoint rule functions this integration implements, beyond the standard library. */
  default List<EndpointFunction> endpointFunctions() {
    return List.of();
  }

  /** Endpoint rule built-ins this integration binds to client configuration. */
  default List<EndpointBuiltIn> endpointBuiltIns() {
    return List.of();
  }

  /**
   * Auth scheme names used in endpoint {@code authSchemes} properties, mapped to the shape id of
   * the auth trait the client's schemes are keyed by.
   */
  default Map<String, String> endpointAuthSchemeIds() {
    return Map.of();
  }

  /**
   * Properties of the service's modeled auth schemes on endpoints whose rules set no {@code
   * authSchemes}.
   */
  default List<EndpointDefaultAuthScheme> endpointDefaultAuthSchemes(ServiceShape service) {
    return List.of();
  }

  /**
   * Writes any helper members needed for a service's endpoint path adapter and returns a C#
   * expression for its thread-safe delegate (ShapeId, string) -> string. The expression is attached
   * to every endpoint result, including direct Resolve calls. At most one integration may supply an
   * adapter for a service.
   */
  default Optional<String> writeEndpointRequestPathAdapter(
      GenerationContext context, ServiceShape service, CSharpWriter writer) {
    return Optional.empty();
  }
}

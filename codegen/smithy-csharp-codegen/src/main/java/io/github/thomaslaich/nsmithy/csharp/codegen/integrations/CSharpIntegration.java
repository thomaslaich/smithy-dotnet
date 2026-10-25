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
import java.util.Set;
import software.amazon.smithy.codegen.core.SmithyIntegration;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public interface CSharpIntegration
    extends SmithyIntegration<CSharpSettings, CSharpWriter, GenerationContext> {

  /**
   * Request/response protocols this integration supplies. A service carrying a protocol's trait
   * gets a client and server that use the protocol's runtime type, as for a built-in protocol.
   */
  default List<CSharpProtocol> protocols() {
    return List.of();
  }

  /**
   * Shapes to generate with {@code service} that the service does not reach through its operations,
   * such as the events it publishes. Each shape is generated with the shapes it references. Called
   * with the model as assembled, before codegen transforms, and only when a service is generated.
   */
  default Set<ShapeId> additionalShapes(
      Model model, CSharpSettings settings, ServiceShape service) {
    return Set.of();
  }

  /**
   * Replaces the generated client and server surface of {@code service}, for a protocol this
   * integration supplies. Shapes, schemas, and operations are still generated as usual. At most one
   * integration may provide a service generator for a service.
   */
  default Optional<CSharpServiceGenerator> serviceGenerator(
      GenerationContext context, ServiceShape service) {
    return Optional.empty();
  }

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
}

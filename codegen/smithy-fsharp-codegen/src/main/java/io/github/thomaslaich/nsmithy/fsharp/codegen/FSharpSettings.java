/*
 * Resolved configuration for the F# codegen plugin.
 *
 * smithy-build.json:
 *   "fsharp-codegen": {
 *     "service": "example.hello#HelloService", // the service whose shapes to generate
 *     "closure": "example.hello#events",       // optional; a shapeClosures metadata entry
 *     "baseNamespace": "MyOrg"                 // optional; prepended to PascalCase(smithyNamespace)
 *   }
 *
 * At least one of service and closure is required. With both, the closure is the generated set
 * and must contain the service.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.node.ExpectationNotMetException;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class FSharpSettings {

  private static final String SERVICE = "service";
  private static final String CLOSURE = "closure";
  private static final String BASE_NAMESPACE = "baseNamespace";

  private final ShapeId service;
  private final String closure;
  private final String baseNamespace;

  private FSharpSettings(ShapeId service, String closure, String baseNamespace) {
    this.service = service;
    this.closure = closure;
    this.baseNamespace = baseNamespace;
  }

  public static FSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(List.of(SERVICE, CLOSURE, BASE_NAMESPACE));
    ShapeId service = config.getStringMember(SERVICE).map(StringNode::expectShapeId).orElse(null);
    String closure = config.getStringMember(CLOSURE).map(StringNode::getValue).orElse(null);
    if (service == null && closure == null) {
      throw new ExpectationNotMetException(
          "fsharp-codegen requires `service`, `closure`, or both.", config);
    }
    return new FSharpSettings(
        service, closure, config.getStringMemberOrDefault(BASE_NAMESPACE, ""));
  }

  /** The service whose shapes to generate. Empty when only a closure is generated. */
  public Optional<ShapeId> service() {
    return Optional.ofNullable(service);
  }

  /** The id of the {@code shapeClosures} metadata entry to generate, when set. */
  public Optional<String> closure() {
    return Optional.ofNullable(closure);
  }

  /** May be empty (no prefix). */
  public String baseNamespace() {
    return baseNamespace;
  }

  /** The F# namespace for a Smithy namespace. */
  public String fsharpNamespace(String smithyNamespace) {
    return Naming.namespaceFor(smithyNamespace, baseNamespace);
  }
}

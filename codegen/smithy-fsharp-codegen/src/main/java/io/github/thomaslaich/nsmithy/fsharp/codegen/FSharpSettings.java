/*
 * Resolved configuration for the F# codegen plugin.
 *
 * smithy-build.json:
 *   "fsharp-codegen": {
 *     "service": "example.hello#HelloService", // optional; the service to generate
 *     "closure": "example.hello#events",       // optional; a shapeClosures metadata entry
 *     "ownedSources": ["/abs/model"],          // optional; the project's own model files
 *     "providedShapes": ["/abs/nsmithy-shapes.json"], // optional; manifests of references
 *     "baseNamespace": "MyOrg",                // optional; prepended to PascalCase(smithyNamespace)
 *     "generateClient": true,                  // optional; emit the service's client (default true)
 *     "generateServer": true,                  // optional; emit the service's server (default true)
 *     "attachValidationException": true,       // optional; declare smithy.framework#ValidationException
 *                                              // on operations with constrained input (default true)
 *     "defaultProtocol": "aws.protocols#restJson1" // optional; the client's default protocol
 *   }
 *
 * At least one of service, closure, and ownedSources is required. With a closure, the closure is
 * the generated set and must contain the service, if there is one. Without a closure, ownedSources
 * makes the project generate the shapes defined in those files, operations included, and with a
 * service also the service and everything it reaches. providedShapes lists the shape manifests of
 * the projects this one references; the shapes they list are not generated again. See
 * designs/shape-ownership.md.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import io.github.thomaslaich.nsmithy.codegen.core.ProvidedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
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
  private static final String OWNED_SOURCES = "ownedSources";
  private static final String PROVIDED_SHAPES = "providedShapes";
  private static final String BASE_NAMESPACE = "baseNamespace";
  private static final String GENERATE_CLIENT = "generateClient";
  private static final String GENERATE_SERVER = "generateServer";
  private static final String ATTACH_VALIDATION_EXCEPTION = "attachValidationException";
  private static final String DEFAULT_PROTOCOL = "defaultProtocol";

  private final ShapeId service;
  private final String closure;
  private final List<String> ownedSources;
  private final List<String> providedShapeManifests;
  private ProvidedShapes providedShapes;
  private final String baseNamespace;
  private final boolean generateClient;
  private final boolean generateServer;
  private final boolean attachValidationException;
  private final ShapeId defaultProtocol;

  private FSharpSettings(
      ShapeId service,
      String closure,
      List<String> ownedSources,
      List<String> providedShapeManifests,
      String baseNamespace,
      boolean generateClient,
      boolean generateServer,
      boolean attachValidationException,
      ShapeId defaultProtocol) {
    this.service = service;
    this.closure = closure;
    this.ownedSources = ownedSources;
    this.providedShapeManifests = providedShapeManifests;
    this.baseNamespace = baseNamespace;
    this.generateClient = generateClient;
    this.generateServer = generateServer;
    this.attachValidationException = attachValidationException;
    this.defaultProtocol = defaultProtocol;
  }

  public static FSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(
        List.of(
            SERVICE,
            CLOSURE,
            OWNED_SOURCES,
            PROVIDED_SHAPES,
            BASE_NAMESPACE,
            GENERATE_CLIENT,
            GENERATE_SERVER,
            ATTACH_VALIDATION_EXCEPTION,
            DEFAULT_PROTOCOL));
    ShapeId service = config.getStringMember(SERVICE).map(StringNode::expectShapeId).orElse(null);
    String closure = config.getStringMember(CLOSURE).map(StringNode::getValue).orElse(null);
    if (service == null && closure == null && config.getArrayMember(OWNED_SOURCES).isEmpty()) {
      throw new ExpectationNotMetException(
          "fsharp-codegen requires `service`, `closure`, or `ownedSources`.", config);
    }
    return new FSharpSettings(
        service,
        closure,
        strings(config, OWNED_SOURCES),
        strings(config, PROVIDED_SHAPES),
        config.getStringMemberOrDefault(BASE_NAMESPACE, ""),
        config.getBooleanMemberOrDefault(GENERATE_CLIENT, true),
        config.getBooleanMemberOrDefault(GENERATE_SERVER, true),
        config.getBooleanMemberOrDefault(ATTACH_VALIDATION_EXCEPTION, true),
        config.getStringMember(DEFAULT_PROTOCOL).map(StringNode::expectShapeId).orElse(null));
  }

  private static List<String> strings(ObjectNode config, String member) {
    return config
        .getArrayMember(member)
        .map(array -> array.getElementsAs(StringNode::getValue))
        .orElse(List.of());
  }

  /** The service to generate. Empty when only a closure or the owned shapes are generated. */
  public Optional<ShapeId> service() {
    return Optional.ofNullable(service);
  }

  /** The id of the {@code shapeClosures} metadata entry to generate, when set. */
  public Optional<String> closure() {
    return Optional.ofNullable(closure);
  }

  /** The project's own model files and directories; empty when the setting is absent. */
  public List<String> ownedSources() {
    return ownedSources;
  }

  /** The shapes referenced projects generated, read from their manifests on first use. */
  public ProvidedShapes providedShapes() {
    if (providedShapes == null) {
      providedShapes = ProvidedShapes.load(providedShapeManifests, ShapeManifest.FSHARP_GENERATOR);
    }
    return providedShapes;
  }

  /**
   * Whether {@code ownedSources} selects the generated set: no closure is set, and either owned
   * sources are listed or there is no service (an empty {@code ownedSources} then generates
   * nothing).
   */
  public boolean generatesOwnedShapes() {
    return closure == null && (!ownedSources.isEmpty() || service == null);
  }

  /** May be empty (no prefix). */
  public String baseNamespace() {
    return baseNamespace;
  }

  /** Whether to emit the service's client. Default true. */
  public boolean generateClient() {
    return generateClient;
  }

  /** Whether to emit the service's server. Default true. */
  public boolean generateServer() {
    return generateServer;
  }

  /**
   * Whether to declare {@code smithy.framework#ValidationException} on every operation whose input
   * is constrained, as the server returns it for them. Default true; when false, a server's
   * constrained operations must declare it in the model. See {@code ValidationErrors}.
   */
  public boolean attachValidationException() {
    return attachValidationException;
  }

  /**
   * The protocol trait of the client's default protocol, when set; otherwise the service's first
   * protocol is the default.
   */
  public Optional<ShapeId> defaultProtocol() {
    return Optional.ofNullable(defaultProtocol);
  }

  /** The F# namespace for a Smithy namespace. */
  public String fsharpNamespace(String smithyNamespace) {
    return Naming.namespaceFor(smithyNamespace, baseNamespace);
  }
}

/*
 * Resolved configuration for the C# codegen plugin.
 *
 * smithy-build.json:
 *   "csharp-codegen": {
 *     "service": "example.hello#HelloService",
 *     "closure": "example.hello#events",  // optional; a shapeClosures metadata entry to generate
 *     "baseNamespace": "MyOrg",         // optional; prepended to PascalCase(smithyNamespace)
 *     "packageVersion": "0.1.0",          // optional
 *     "generateClient": true,             // optional; emit the client surface (default true)
 *     "generateServer": true,             // optional; emit the server surface (default true)
 *     "generateDependencyInjection": true, // optional; emit the IHttpClientFactory extension
 *     "generateFakes": false,             // optional; emit fakes per surface (default false)
 *     "defaultProtocol": "aws.protocols#restJson1" // optional; the client's default protocol
 *   }
 *
 * At least one of service and closure is required. A closure alone generates only the types in
 * it. With both, the closure is the generated set and the service, which must be in it, gets its
 * client and server.
 *
 * If baseNamespace is omitted, the C# namespace is just PascalCase of the
 * Smithy namespace (e.g. example.hello -> Example.Hello).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Naming;
import java.util.Optional;
import software.amazon.smithy.model.node.ExpectationNotMetException;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpSettings {

  private static final String SERVICE = "service";
  private static final String CLOSURE = "closure";
  private static final String BASE_NAMESPACE = "baseNamespace";
  private static final String PACKAGE_VERSION = "packageVersion";
  private static final String GENERATE_CLIENT = "generateClient";
  private static final String GENERATE_SERVER = "generateServer";
  private static final String GENERATE_DEPENDENCY_INJECTION = "generateDependencyInjection";
  private static final String GENERATE_FAKES = "generateFakes";
  private static final String DEFAULT_PROTOCOL = "defaultProtocol";

  private final ShapeId service;
  private final String closure;
  private final String baseNamespace;
  private final String packageVersion;
  private final boolean generateClient;
  private final boolean generateServer;
  private final boolean generateDependencyInjection;
  private final boolean generateFakes;
  private final ShapeId defaultProtocol;

  private CSharpSettings(
      ShapeId service,
      String closure,
      String baseNamespace,
      String packageVersion,
      boolean generateClient,
      boolean generateServer,
      boolean generateDependencyInjection,
      boolean generateFakes,
      ShapeId defaultProtocol) {
    this.service = service;
    this.closure = closure;
    this.baseNamespace = baseNamespace;
    this.packageVersion = packageVersion;
    this.generateClient = generateClient;
    this.generateServer = generateServer;
    this.generateDependencyInjection = generateDependencyInjection;
    this.generateFakes = generateFakes;
    this.defaultProtocol = defaultProtocol;
  }

  public static CSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(
        java.util.List.of(
            SERVICE,
            CLOSURE,
            BASE_NAMESPACE,
            PACKAGE_VERSION,
            GENERATE_CLIENT,
            GENERATE_SERVER,
            GENERATE_DEPENDENCY_INJECTION,
            GENERATE_FAKES,
            DEFAULT_PROTOCOL));
    ShapeId service =
        config.getStringMember(SERVICE).map(node -> node.expectShapeId()).orElse(null);
    String closure = config.getStringMember(CLOSURE).map(node -> node.getValue()).orElse(null);
    if (service == null && closure == null) {
      throw new ExpectationNotMetException(
          "csharp-codegen requires `service`, `closure`, or both.", config);
    }
    String baseNamespace = config.getStringMemberOrDefault(BASE_NAMESPACE, "");
    String packageVersion = config.getStringMemberOrDefault(PACKAGE_VERSION, "0.0.1");
    boolean generateClient = config.getBooleanMemberOrDefault(GENERATE_CLIENT, true);
    boolean generateServer = config.getBooleanMemberOrDefault(GENERATE_SERVER, true);
    boolean generateDependencyInjection =
        config.getBooleanMemberOrDefault(GENERATE_DEPENDENCY_INJECTION, false);
    boolean generateFakes = config.getBooleanMemberOrDefault(GENERATE_FAKES, false);
    ShapeId defaultProtocol =
        config.getStringMember(DEFAULT_PROTOCOL).map(node -> node.expectShapeId()).orElse(null);
    return new CSharpSettings(
        service,
        closure,
        baseNamespace,
        packageVersion,
        generateClient,
        generateServer,
        generateDependencyInjection,
        generateFakes,
        defaultProtocol);
  }

  /** The service to generate a client and server for. Empty when only a closure is generated. */
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

  public String packageVersion() {
    return packageVersion;
  }

  /** Whether to emit the client surface (client, operation bindings). Default true. */
  public boolean generateClient() {
    return generateClient;
  }

  /** Whether to emit the server surface (handlers, endpoint extensions). Default true. */
  public boolean generateServer() {
    return generateServer;
  }

  /** Whether to emit the opt-in IHttpClientFactory registration extension. */
  public boolean generateDependencyInjection() {
    return generateDependencyInjection;
  }

  /** Whether to emit the opt-in fake handler ({Service}.Fakes.g.cs). Requires generateServer. */
  public boolean generateFakes() {
    return generateFakes;
  }

  /**
   * The protocol trait of the client's default protocol, when set; otherwise the service's first
   * protocol is the default.
   */
  public Optional<ShapeId> defaultProtocol() {
    return Optional.ofNullable(defaultProtocol);
  }

  /** Convenience: C# namespace for a given Smithy namespace. */
  public String csharpNamespace(String smithyNamespace) {
    return Naming.namespaceFor(smithyNamespace, baseNamespace);
  }
}

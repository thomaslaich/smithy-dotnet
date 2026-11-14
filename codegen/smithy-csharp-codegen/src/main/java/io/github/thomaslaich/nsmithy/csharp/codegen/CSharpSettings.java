/*
 * Resolved configuration for the C# codegen plugin.
 *
 * smithy-build.json:
 *   "csharp-codegen": {
 *     "service": "example.hello#HelloService",
 *     "closure": "example.hello#events",  // optional; a shapeClosures metadata entry to generate
 *     "ownedSources": ["/abs/model"],      // optional; the project's own model files and directories
 *     "providedShapes": ["/abs/nsmithy-shapes.json"], // optional; manifests of referenced projects
 *     "baseNamespace": "MyOrg",         // optional; prepended to PascalCase(smithyNamespace)
 *     "packageVersion": "0.1.0",          // optional
 *     "generateClient": true,             // optional; emit the client surface (default true)
 *     "generateServer": true,             // optional; emit the server surface (default true)
 *     "generateDependencyInjection": true, // optional; emit the IHttpClientFactory extension
 *     "generateFakes": false,             // optional; emit fakes per surface (default false)
 *     "generateSmokeTests": false,        // optional; emit @smokeTests cases (default false)
 *     "defaultProtocol": "aws.protocols#restJson1" // optional; the client's default protocol
 *   }
 *
 * At least one of service, closure, and ownedSources is required. A closure alone generates only
 * the types in it. With both, the closure is the generated set and the service, which must be in it,
 * gets its client and server.
 *
 * Without a closure, ownedSources makes the project generate the shapes defined in those files,
 * operations included with their schemas and handler interfaces, and everything they target. With a
 * service, the service and everything it reaches are generated too. See designs/shape-ownership.md.
 *
 * providedShapes lists the shape manifests of the projects this one references. The shapes they list
 * are not generated again; generated code refers to the types those projects generated.
 *
 * If baseNamespace is omitted, the C# namespace is just PascalCase of the
 * Smithy namespace (e.g. example.hello -> Example.Hello).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

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
public final class CSharpSettings {

  private static final String SERVICE = "service";
  private static final String CLOSURE = "closure";
  private static final String OWNED_SOURCES = "ownedSources";
  private static final String PROVIDED_SHAPES = "providedShapes";
  private static final String BASE_NAMESPACE = "baseNamespace";
  private static final String PACKAGE_VERSION = "packageVersion";
  private static final String GENERATE_CLIENT = "generateClient";
  private static final String GENERATE_SERVER = "generateServer";
  private static final String GENERATE_DEPENDENCY_INJECTION = "generateDependencyInjection";
  private static final String GENERATE_FAKES = "generateFakes";
  private static final String GENERATE_SMOKE_TESTS = "generateSmokeTests";
  private static final String DEFAULT_PROTOCOL = "defaultProtocol";

  private final ShapeId service;
  private final String closure;
  private final List<String> ownedSources;
  private final List<String> providedShapeManifests;
  private ProvidedShapes providedShapes;
  private final String baseNamespace;
  private final String packageVersion;
  private final boolean generateClient;
  private final boolean generateServer;
  private final boolean generateDependencyInjection;
  private final boolean generateFakes;
  private final boolean generateSmokeTests;
  private final ShapeId defaultProtocol;

  private CSharpSettings(
      ShapeId service,
      String closure,
      List<String> ownedSources,
      List<String> providedShapeManifests,
      String baseNamespace,
      String packageVersion,
      boolean generateClient,
      boolean generateServer,
      boolean generateDependencyInjection,
      boolean generateFakes,
      boolean generateSmokeTests,
      ShapeId defaultProtocol) {
    this.service = service;
    this.closure = closure;
    this.ownedSources = ownedSources;
    this.providedShapeManifests = providedShapeManifests;
    this.baseNamespace = baseNamespace;
    this.packageVersion = packageVersion;
    this.generateClient = generateClient;
    this.generateServer = generateServer;
    this.generateDependencyInjection = generateDependencyInjection;
    this.generateFakes = generateFakes;
    this.generateSmokeTests = generateSmokeTests;
    this.defaultProtocol = defaultProtocol;
  }

  public static CSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(
        List.of(
            SERVICE,
            CLOSURE,
            OWNED_SOURCES,
            PROVIDED_SHAPES,
            BASE_NAMESPACE,
            PACKAGE_VERSION,
            GENERATE_CLIENT,
            GENERATE_SERVER,
            GENERATE_DEPENDENCY_INJECTION,
            GENERATE_FAKES,
            GENERATE_SMOKE_TESTS,
            DEFAULT_PROTOCOL));
    ShapeId service =
        config.getStringMember(SERVICE).map(node -> node.expectShapeId()).orElse(null);
    String closure = config.getStringMember(CLOSURE).map(node -> node.getValue()).orElse(null);
    List<String> ownedSources =
        config
            .getArrayMember(OWNED_SOURCES)
            .map(array -> array.getElementsAs(StringNode::getValue))
            .orElse(List.of());
    List<String> providedShapeManifests =
        config
            .getArrayMember(PROVIDED_SHAPES)
            .map(array -> array.getElementsAs(StringNode::getValue))
            .orElse(List.of());
    if (service == null && closure == null && config.getArrayMember(OWNED_SOURCES).isEmpty()) {
      throw new ExpectationNotMetException(
          "csharp-codegen requires `service`, `closure`, or `ownedSources`.", config);
    }
    String baseNamespace = config.getStringMemberOrDefault(BASE_NAMESPACE, "");
    String packageVersion = config.getStringMemberOrDefault(PACKAGE_VERSION, "0.0.1");
    boolean generateClient = config.getBooleanMemberOrDefault(GENERATE_CLIENT, true);
    boolean generateServer = config.getBooleanMemberOrDefault(GENERATE_SERVER, true);
    boolean generateDependencyInjection =
        config.getBooleanMemberOrDefault(GENERATE_DEPENDENCY_INJECTION, false);
    boolean generateFakes = config.getBooleanMemberOrDefault(GENERATE_FAKES, false);
    boolean generateSmokeTests = config.getBooleanMemberOrDefault(GENERATE_SMOKE_TESTS, false);
    ShapeId defaultProtocol =
        config.getStringMember(DEFAULT_PROTOCOL).map(node -> node.expectShapeId()).orElse(null);
    return new CSharpSettings(
        service,
        closure,
        ownedSources,
        providedShapeManifests,
        baseNamespace,
        packageVersion,
        generateClient,
        generateServer,
        generateDependencyInjection,
        generateFakes,
        generateSmokeTests,
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

  /**
   * The project's own model files and directories. The project generates the shapes defined in
   * them; empty when the setting is absent.
   */
  public List<String> ownedSources() {
    return ownedSources;
  }

  /** The shape manifests of referenced projects, as configured. */
  public List<String> providedShapeManifests() {
    return providedShapeManifests;
  }

  /** The shapes referenced projects generated, read from their manifests on first use. */
  public ProvidedShapes providedShapes() {
    if (providedShapes == null) {
      providedShapes = ProvidedShapes.load(providedShapeManifests, ShapeManifest.CSHARP_GENERATOR);
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

  /** Whether to generate the service's {@code @smokeTests} cases alongside the client. */
  public boolean generateSmokeTests() {
    return generateSmokeTests;
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

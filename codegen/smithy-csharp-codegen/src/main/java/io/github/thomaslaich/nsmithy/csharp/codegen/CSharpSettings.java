/*
 * Resolved configuration for the C# codegen plugin.
 *
 * smithy-build.json:
 *   "csharp-codegen": {
 *     "service": "example.hello#HelloService",
 *     "baseNamespace": "MyOrg",         // optional; prepended to PascalCase(smithyNamespace)
 *     "packageVersion": "0.1.0",          // optional
 *     "generateClient": true,             // optional; emit the client surface (default true)
 *     "generateServer": true,             // optional; emit the server surface (default true)
 *     "generateDependencyInjection": true, // optional; emit the IHttpClientFactory extension
 *     "generateFakes": false              // optional; emit the fake handler (default false)
 *   }
 *
 * If baseNamespace is omitted, the C# namespace is just PascalCase of the
 * Smithy namespace (e.g. example.hello -> Example.Hello).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpSettings {

  private static final String SERVICE = "service";
  private static final String BASE_NAMESPACE = "baseNamespace";
  private static final String PACKAGE_VERSION = "packageVersion";
  private static final String GENERATE_CLIENT = "generateClient";
  private static final String GENERATE_SERVER = "generateServer";
  private static final String GENERATE_DEPENDENCY_INJECTION = "generateDependencyInjection";
  private static final String GENERATE_FAKES = "generateFakes";

  private final ShapeId service;
  private final String baseNamespace;
  private final String packageVersion;
  private final boolean generateClient;
  private final boolean generateServer;
  private final boolean generateDependencyInjection;
  private final boolean generateFakes;

  private CSharpSettings(
      ShapeId service,
      String baseNamespace,
      String packageVersion,
      boolean generateClient,
      boolean generateServer,
      boolean generateDependencyInjection,
      boolean generateFakes) {
    this.service = service;
    this.baseNamespace = baseNamespace;
    this.packageVersion = packageVersion;
    this.generateClient = generateClient;
    this.generateServer = generateServer;
    this.generateDependencyInjection = generateDependencyInjection;
    this.generateFakes = generateFakes;
  }

  public static CSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(
        java.util.List.of(
            SERVICE,
            BASE_NAMESPACE,
            PACKAGE_VERSION,
            GENERATE_CLIENT,
            GENERATE_SERVER,
            GENERATE_DEPENDENCY_INJECTION,
            GENERATE_FAKES));
    ShapeId service = config.expectStringMember(SERVICE).expectShapeId();
    String baseNamespace = config.getStringMemberOrDefault(BASE_NAMESPACE, "");
    String packageVersion = config.getStringMemberOrDefault(PACKAGE_VERSION, "0.0.1");
    boolean generateClient = config.getBooleanMemberOrDefault(GENERATE_CLIENT, true);
    boolean generateServer = config.getBooleanMemberOrDefault(GENERATE_SERVER, true);
    boolean generateDependencyInjection =
        config.getBooleanMemberOrDefault(GENERATE_DEPENDENCY_INJECTION, false);
    boolean generateFakes = config.getBooleanMemberOrDefault(GENERATE_FAKES, false);
    return new CSharpSettings(
        service,
        baseNamespace,
        packageVersion,
        generateClient,
        generateServer,
        generateDependencyInjection,
        generateFakes);
  }

  public ShapeId service() {
    return service;
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

  /** Convenience: C# namespace for a given Smithy namespace. */
  public String csharpNamespace(String smithyNamespace) {
    return CSharpNaming.namespaceFor(smithyNamespace, baseNamespace);
  }
}

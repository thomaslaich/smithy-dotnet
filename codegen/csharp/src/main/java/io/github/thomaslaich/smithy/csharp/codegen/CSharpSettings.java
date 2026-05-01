/*
 * Resolved configuration for the C# codegen plugin.
 *
 * smithy-build.json:
 *   "csharp-codegen": {
 *     "service": "example.hello#HelloService",
 *     "baseNamespace": "MyOrg",         // optional; prepended to PascalCase(smithyNamespace)
 *     "packageVersion": "0.1.0"           // optional
 *   }
 *
 * If baseNamespace is omitted, the C# namespace is just PascalCase of the
 * Smithy namespace (e.g. example.hello -> Example.Hello).
 */
package io.github.thomaslaich.smithy.csharp.codegen;

import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class CSharpSettings {

  private static final String SERVICE = "service";
  private static final String BASE_NAMESPACE = "baseNamespace";
  private static final String PACKAGE_VERSION = "packageVersion";

  private final ShapeId service;
  private final String baseNamespace;
  private final String packageVersion;

  private CSharpSettings(ShapeId service, String baseNamespace, String packageVersion) {
    this.service = service;
    this.baseNamespace = baseNamespace;
    this.packageVersion = packageVersion;
  }

  public static CSharpSettings fromNode(ObjectNode config) {
    config.warnIfAdditionalProperties(java.util.List.of(SERVICE, BASE_NAMESPACE, PACKAGE_VERSION));
    ShapeId service = config.expectStringMember(SERVICE).expectShapeId();
    String baseNamespace = config.getStringMemberOrDefault(BASE_NAMESPACE, "");
    String packageVersion = config.getStringMemberOrDefault(PACKAGE_VERSION, "0.0.1");
    return new CSharpSettings(service, baseNamespace, packageVersion);
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

  /** Convenience: C# namespace for a given Smithy namespace. */
  public String csharpNamespace(String smithyNamespace) {
    return CSharpNaming.namespaceFor(smithyNamespace, baseNamespace);
  }
}

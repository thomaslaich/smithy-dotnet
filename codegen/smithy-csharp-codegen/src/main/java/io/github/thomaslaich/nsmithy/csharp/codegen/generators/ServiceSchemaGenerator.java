/*
 * Emits the runtime ServiceSchema for a service shape:
 *
 *   public static partial class {Service}Schema
 *   {
 *       public static ServiceSchema Schema { get; } =
 *           Schemas.Service(ShapeId.Parse("ns#Service"), "2020-01-01", traits);
 *   }
 *
 * The service schema is service-scoped (carries the service shape id + service-level traits) and is
 * consumed by protocols to derive service-named wire artifacts (e.g. the rpcv2Cbor request path).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ServiceSchemaGenerator implements Runnable {

  private final CSharpWriter writer;
  private final ServiceShape service;

  public ServiceSchemaGenerator(CSharpWriter w, ServiceShape s) {
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);

    String typeName = CSharpNaming.typeName(service.getId().getName());
    writer.write("public static partial class $LSchema", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static ServiceSchema Schema { get; } =");
          writer.indent();
          writer.write(
              "Schemas.Service($L, $S, $L);",
              SchemaGenerator.shapeIdExpr(service.getId()),
              service.getVersion(),
              SchemaGenerator.traitsExpr(service.getAllTraits().values()));
          writer.dedent();
        });
  }
}

/*
 * Emits the runtime ServiceSchema for a service shape:
 *
 *   public static partial class {Service}Schema
 *   {
 *       public static ServiceSchema Schema { get; } =
 *           Schemas.Service(ShapeId.Parse("ns#Service"), "2020-01-01", traits);
 *
 *       // An operation another project generated, with the service's errors added.
 *       public static OperationSchema<Input, Output> {Operation}Operation { get; } =
 *           {Operation}Schema.Schema.WithErrors([...]);
 *   }
 *
 * The service schema is service-scoped (carries the service shape id + service-level traits) and is
 * consumed by protocols to derive service-named wire artifacts (e.g. the rpcv2Cbor request path).
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.function.Consumer;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ServiceSchemaGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public ServiceSchemaGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    writer.pushState();
    try {
      writer.putContext("typeName", CSharpNaming.typeName(service.getId().getName()));
      writer.putContext("serviceSchema", RuntimeTypes.SERVICE_SCHEMA);
      writer.putContext("schemas", RuntimeTypes.SCHEMAS);
      writer.putContext("shapeId", SchemaGenerator.shapeIdExpr(writer, service.getId()));
      writer.putContext("version", service.getVersion());
      writer.putContext(
          "traits", SchemaGenerator.traitsExpr(writer, service.getAllTraits().values()));
      writer.putContext("operations", writer.consumer(operationsWithServiceErrors()));
      writer.write(
          """
          public static partial class ${typeName:L}Schema
          {
              public static ${serviceSchema:T} Schema { get; } =
                  ${schemas:T}.Service(${shapeId:L}, ${version:S}, ${traits:L});
              ${operations:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private Consumer<CSharpWriter> operationsWithServiceErrors() {
    return w -> {
      List<ShapeId> errors = service.getErrors().stream().sorted().toList();
      for (OperationShape op : TopDownIndex.of(context.model()).getContainedOperations(service)) {
        if (SchemaGenerator.addsServiceErrors(context, service, op)) {
          w.write("");
          w.write(
              "public static $T<$L, $L> $L { get; } = $L.Schema.WithErrors($L);",
              RuntimeTypes.OPERATION_SCHEMA,
              SchemaGenerator.operationShapeType(w, context, op.getInputShape()),
              SchemaGenerator.operationShapeType(w, context, op.getOutputShape()),
              SchemaGenerator.serviceOperationProperty(op),
              SchemaGenerator.schemaClassName(w, context, op),
              OperationSchemaGenerator.errorsLiteral(w, context, errors));
        }
      }
    };
  }
}

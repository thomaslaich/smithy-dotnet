package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class OperationSchemaGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final OperationShape shape;

  public OperationSchemaGenerator(GenerationContext c, CSharpWriter w, OperationShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);

    String typeName = CSharpNaming.typeName(shape.getId().getName());
    writer.write("public static partial class $LSchema", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public static FunctionalOperationSchema<$L, $L> FunctionalSchema { get; } =",
              SchemaGenerator.functionalOperationShapeType(context, shape.getInputShape()),
              SchemaGenerator.functionalOperationShapeType(context, shape.getOutputShape()));
          writer.indent();
          writer.write(
              "FunctionalSchemas.Operation($L, $L, $L, $L);",
              SchemaGenerator.shapeIdExpr(shape.getId()),
              SchemaGenerator.functionalOperationShapeSchema(context, shape.getInputShape()),
              SchemaGenerator.functionalOperationShapeSchema(context, shape.getOutputShape()),
              SchemaGenerator.traitsExpr(shape.getAllTraits().values()));
          writer.dedent();
        });
  }
}

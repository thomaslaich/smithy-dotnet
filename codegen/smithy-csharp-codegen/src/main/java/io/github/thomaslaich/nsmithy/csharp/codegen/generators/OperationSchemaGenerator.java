package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.HttpErrorTrait;
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
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    List<ShapeId> errors =
        shape.getErrors().stream()
            .sorted(Comparator.comparing(ShapeId::toString))
            .collect(Collectors.toList());
    boolean isStreaming =
        ShapeSupport.isStreamingShape(context.model(), shape.getInputShape())
            || ShapeSupport.isStreamingShape(context.model(), shape.getOutputShape());
    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("operationSchema", RuntimeTypes.OPERATION_SCHEMA);
      writer.putContext("schemas", RuntimeTypes.SCHEMAS);
      writer.putContext(
          "inputType", SchemaGenerator.operationShapeType(writer, context, shape.getInputShape()));
      writer.putContext(
          "outputType",
          SchemaGenerator.operationShapeType(writer, context, shape.getOutputShape()));
      writer.putContext("shapeId", SchemaGenerator.shapeIdExpr(writer, shape.getId()));
      writer.putContext(
          "inputSchema",
          SchemaGenerator.operationShapeSchema(writer, context, shape.getInputShape()));
      writer.putContext(
          "outputSchema",
          SchemaGenerator.operationShapeSchema(writer, context, shape.getOutputShape()));
      writer.putContext("errors", errorsLiteral(errors));
      writer.putContext(
          "traits", SchemaGenerator.traitsExpr(writer, shape.getAllTraits().values()));
      writer.putContext("streamingArgument", isStreaming ? ", isStreaming: true" : "");
      writer.write(
          """
          public static partial class ${typeName:L}Schema
          {
              public static ${operationSchema:T}<${inputType:L}, ${outputType:L}> Schema { get; } =
                  ${schemas:T}.Operation(${shapeId:L}, ${inputSchema:L}, ${outputSchema:L}, ${errors:L}, ${traits:L}${streamingArgument:L});
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private String errorsLiteral(List<ShapeId> errors) {
    if (errors.isEmpty()) {
      return "[]";
    }

    return "["
        + errors.stream()
            .map(
                errorId -> {
                  StructureShape error = context.model().expectShape(errorId, StructureShape.class);
                  return (writer.typeName(RuntimeTypes.SCHEMAS) + ".OperationError(")
                      + SchemaGenerator.shapeIdExpr(writer, errorId)
                      + ", "
                      + SchemaGenerator.shapeSchemaAccessor(writer, context, error)
                      + ", "
                      + httpErrorCode(error)
                      + ")";
                })
            .collect(Collectors.joining(", "))
        + "]";
  }

  private static int httpErrorCode(StructureShape error) {
    var awsQueryError = error.findTrait(TraitIds.AWS_QUERY_ERROR);
    if (awsQueryError.isPresent()) {
      return awsQueryError
          .get()
          .toNode()
          .expectObjectNode()
          .getMember("httpResponseCode")
          .orElseThrow()
          .expectNumberNode()
          .getValue()
          .intValue();
    }
    return error
        .getTrait(HttpErrorTrait.class)
        .map(HttpErrorTrait::getCode)
        .orElseGet(
            () ->
                error
                    .getTrait(ErrorTrait.class)
                    .map(trait -> trait.isClientError() ? 400 : 500)
                    .orElse(500));
  }
}

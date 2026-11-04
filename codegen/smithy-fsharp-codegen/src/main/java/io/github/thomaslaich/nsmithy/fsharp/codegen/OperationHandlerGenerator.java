package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Declares an operation's handler interface in Operations.g.fs: {@code I{Operation}Handler}, in the
 * operation's namespace, with one method that takes the operation's input and returns its output.
 * It depends only on the operation, so the project that owns the operation declares it, whether or
 * not it generates a server; the service's server combines the interfaces of its operations.
 */
@SmithyInternalApi
final class OperationHandlerGenerator {

  private static final String TASK = "global.System.Threading.Tasks.Task";
  private static final String CANCELLATION_TOKEN = "global.System.Threading.CancellationToken";

  private final FSharpContext context;
  private final SchemaGenerator schemas;

  OperationHandlerGenerator(FSharpContext context) {
    this.context = context;
    this.schemas = new SchemaGenerator(context);
  }

  void run(OperationShape operation) {
    List<String> parameters = new ArrayList<>();
    if (!ShapeSupport.isUnit(operation.getInputShape())) {
      parameters.add("input: " + schemas.operationType(operation.getInputShape()));
    }
    parameters.add("cancellationToken: " + CANCELLATION_TOKEN);
    context
        .files()
        .handlers(operation)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            type ${handler:L} =
                ${?docs}
                ${docs:L|}
                ${/docs}
                abstract ${method:L}: ${parameters:L} -> ${returns:L}
            """,
            Map.of(
                "docs", FSharpWriter.docs(operation),
                "handler", context.symbolProvider().handlerSymbol(operation).getName(),
                "method", methodName(operation),
                "parameters", String.join(" * ", parameters),
                "returns",
                    ShapeSupport.isUnit(operation.getOutputShape())
                        ? TASK
                        : TASK + "<" + schemas.operationType(operation.getOutputShape()) + ">"));
  }

  /** The name of a handler interface's method. */
  static String methodName(OperationShape operation) {
    return FSharpNaming.typeName(operation.getId().getName()) + "Async";
  }
}

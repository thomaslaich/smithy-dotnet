package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Emits an operation's handler interface, {@code I{Operation}Handler}, with one method that takes
 * the operation's input and returns its output. The interface depends only on the operation, so
 * each one has a file of its own; the service's server file combines them into {@code
 * I{Service}ServiceHandler}.
 */
@SmithyInternalApi
public final class OperationHandlerGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final OperationShape operation;

  public OperationHandlerGenerator(GenerationContext c, CSharpWriter w, OperationShape op) {
    this.context = c;
    this.writer = w;
    this.operation = op;
  }

  /** The name of an operation's handler interface. */
  public static String handlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  /**
   * The symbol of an operation's handler interface. It lives in the operation's namespace, in a
   * file of its own: {@code {Operation}.Handler.g.cs}. It has no ".Server" suffix, because it
   * depends only on the operation and the project that owns the operation always compiles it.
   */
  public static Symbol handlerSymbol(CSharpSettings settings, OperationShape op) {
    Optional<Symbol> provided = settings.providedShapes().handler(op.getId());
    if (provided.isPresent()) {
      return provided.get();
    }
    String namespace = settings.csharpNamespace(op.getId().getNamespace());
    String file =
        namespace.replace('.', '/')
            + "/"
            + CSharpNaming.typeName(op.getId().getName())
            + ".Handler.g.cs";
    return Symbol.builder()
        .name(handlerName(op))
        .namespace(namespace, ".")
        .definitionFile(file)
        .build();
  }

  @Override
  public void run() {
    Map<String, String> parameterDocs = operationParameterDocs();
    writer.writeXmlDocs(operation, parameterDocs);
    writer.pushState();
    try {
      writer.putContext("handler", handlerName(operation));
      writer.putContext(
          "operation",
          writer.consumer(
              w -> {
                w.writeXmlDocs(operation, parameterDocs);
                w.write("$L;", signature(context.symbolProvider()));
              }));
      writer.write(
          """
          public interface ${handler:L}
          {
              ${operation:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private String signature(SymbolProvider sp) {
    Model model = context.model();
    boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(operation.getOutputShape());
    String name = CSharpNaming.typeName(operation.getId().getName()) + "Async";
    String inputType =
        hasInput
            ? writer.typeName(sp.toSymbol(model.expectShape(operation.getInputShape())))
            : null;
    String outputType =
        hasOutput
            ? writer.typeName(sp.toSymbol(model.expectShape(operation.getOutputShape())))
            : null;
    String returnType =
        hasOutput
            ? writer.typeName(RuntimeTypes.TASK) + "<" + outputType + ">"
            : writer.typeName(RuntimeTypes.TASK);
    String params = hasInput ? inputType + " input, " : "";
    return writer.format(
        "$L $L($L$T cancellationToken = default)",
        returnType,
        name,
        params,
        RuntimeTypes.CANCELLATION_TOKEN);
  }

  private Map<String, String> operationParameterDocs() {
    if (ShapeSupport.isUnit(operation.getInputShape())) {
      return Map.of();
    }
    Map<String, String> docs = new LinkedHashMap<>();
    context
        .model()
        .expectShape(operation.getInputShape())
        .getTrait(DocumentationTrait.class)
        .ifPresent(trait -> docs.put("input", trait.getValue()));
    return docs;
  }
}

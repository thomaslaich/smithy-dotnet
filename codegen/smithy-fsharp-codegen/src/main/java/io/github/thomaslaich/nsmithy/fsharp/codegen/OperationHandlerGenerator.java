package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Declares an operation's error union and handler interface in Operations.g.fs, in the operation's
 * namespace: {@code {Operation}Error}, one case per error the operation declares, which its handler
 * and clients return in a Result; and {@code I{Operation}Handler}, with one method that takes the
 * operation's input and returns its output. Both depend only on the operation, so the project that
 * owns the operation declares them, whether or not it generates a server; the service's server
 * combines the interfaces of its operations.
 */
@SmithyInternalApi
final class OperationHandlerGenerator {

  private static final String TASK = "global.System.Threading.Tasks.Task";
  private static final String CANCELLATION_TOKEN = "global.System.Threading.CancellationToken";
  private static final String RESULT = "global.Microsoft.FSharp.Core.Result";
  private static final String TO_EXCEPTION = "ToException";
  private static final String FROM_EXCEPTION = "FromException";
  private static final String OPTION = "global.Microsoft.FSharp.Core.Option";

  private final FSharpContext context;
  private final SchemaGenerator schemas;

  OperationHandlerGenerator(FSharpContext context) {
    this.context = context;
    this.schemas = new SchemaGenerator(context);
  }

  /** Declares the operation's error union, when it declares errors. */
  void errors(OperationShape operation) {
    Optional<Symbol> symbol = context.symbolProvider().errorsSymbol(operation);
    if (symbol.isEmpty()) {
      return;
    }
    String type = symbol.get().getName();
    checkName(operation, symbol.get());
    List<String> cases = new ArrayList<>();
    List<String> clauses = new ArrayList<>();
    List<String> fromClauses = new ArrayList<>();
    Map<String, StructureShape> byCase = new HashMap<>();
    for (StructureShape error : errorShapes(context.model(), operation)) {
      String name = caseName(error);
      StructureShape clash = byCase.put(name, error);
      if (clash != null || name.equals(TO_EXCEPTION) || name.equals(FROM_EXCEPTION)) {
        throw new CodegenException(
            "Error "
                + error.getId()
                + " of operation "
                + operation.getId()
                + " would be the case "
                + name
                + " of "
                + type
                + (clash != null
                    ? ", like " + clash.getId()
                    : ", which clashes with its " + name + " method")
                + ". Rename the error, for example with the renameShapes build transform.");
      }
      cases.add("| " + name + " of " + context.symbolProvider().typeOf(error));
      clauses.add("| " + type + "." + name + " error -> error :> global.System.Exception");
      fromClauses.add(
          "| :? "
              + context.symbolProvider().typeOf(error)
              + " as error -> "
              + OPTION
              + ".Some("
              + type
              + "."
              + name
              + " error)");
    }
    context
        .files()
        .handlers(operation)
        .writeTemplate(
            """
            /// <summary>The errors the model declares for <c>${operation:L}</c>.</summary>
            [<RequireQualifiedAccess>]
            type ${type:L} =
                ${#cases}
                ${value:L}
                ${/cases}

                /// <summary>The error as the exception the runtime writes.</summary>
                member this.${toException:L}() : global.System.Exception =
                    match this with
                    ${#clauses}
                    ${value:L}
                    ${/clauses}

                /// <summary>The case that holds <c>error</c>, or None when the operation does not declare it.</summary>
                static member ${fromException:L}(error: global.System.Exception) : ${type:L} option =
                    match error with
                    ${#fromClauses}
                    ${value:L}
                    ${/fromClauses}
                    | _ -> ${option:L}.None
            """,
            Map.of(
                "operation",
                FSharpNaming.typeName(operation.getId().getName()),
                "type",
                type,
                "cases",
                cases,
                "clauses",
                clauses,
                "fromClauses",
                fromClauses,
                "fromException",
                FROM_EXCEPTION,
                "option",
                OPTION,
                "toException",
                TO_EXCEPTION));
  }

  /** Declares the operation's handler interface. */
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
                "returns", returnType(operation)));
  }

  /**
   * What a handler returns: the operation's output, in a Result with its error union when it
   * declares errors.
   */
  private String returnType(OperationShape operation) {
    boolean unit = ShapeSupport.isUnit(operation.getOutputShape());
    String output = schemas.operationType(operation.getOutputShape());
    Optional<Symbol> errors = context.symbolProvider().errorsSymbol(operation);
    if (errors.isPresent()) {
      // The union is declared just above, in the same namespace section, which does not resolve
      // its own types by their qualified names; a provided union is another assembly's.
      boolean provided = context.settings().providedShapes().contains(operation.getId());
      return TASK
          + "<"
          + RESULT
          + "<"
          + (unit ? "unit" : output)
          + ", "
          + (provided ? FSharpSymbolProvider.render(errors.get()) : errors.get().getName())
          + ">>";
    }
    return unit ? TASK : TASK + "<" + output + ">";
  }

  /** The name of a handler interface's method. */
  static String methodName(OperationShape operation) {
    return FSharpNaming.typeName(operation.getId().getName()) + "Async";
  }

  /** The method of an error union that returns the case holding an exception, if any. */
  static String fromExceptionMethod() {
    return FROM_EXCEPTION;
  }

  /** The method of an error union that returns the error as its exception. */
  static String toExceptionMethod() {
    return TO_EXCEPTION;
  }

  /** The case of an operation's error union that holds {@code error}. */
  static String caseName(StructureShape error) {
    return FSharpNaming.typeName(error.getId().getName());
  }

  /** The errors an operation declares, in a stable order. */
  static List<StructureShape> errorShapes(Model model, OperationShape operation) {
    return operation.getErrors().stream()
        .sorted()
        .map(error -> model.expectShape(error, StructureShape.class))
        .toList();
  }

  /** Fails when a model shape's F# type would have the union's name. */
  private void checkName(OperationShape operation, Symbol union) {
    GeneratedNames.declaring(context, union.getNamespace(), union.getName())
        .ifPresent(shape -> {
          throw new CodegenException(
              "Shape "
                  + shape.getId()
                  + " would generate an F# type named "
                  + union.getName()
                  + ", which holds the errors of "
                  + operation.getId()
                  + ". Rename the shape, for example with the renameShapes build transform.");
        });
  }
}

/*
 * Fake handler generator, opt-in via generateFakes. Emits:
 *   - `Fake{Service}Handler : I{Service}Handler` whose methods return canned responses
 *     synthesized by FakeValueSynthesizer (the first non-error @examples output when present,
 *     deterministic placeholders otherwise).
 *
 * The class is registered through the ordinary Add{Service}Handler<T>() extension. Its operation
 * methods are virtual so a subclass can replace individual operations; registering a real
 * per-operation handler after the fake works too, since the last DI registration wins.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class FakeHandlerGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final FakeValueSynthesizer values;

  public FakeHandlerGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.values = new FakeValueSynthesizer(c, "fake handler");
  }

  @Override
  public void run() {
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> ops =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());

    writer.addImport(RuntimeTypes.NSMITHY_CORE);

    String serviceTypeName = CSharpNaming.typeName(service.getId().getName());
    String contract =
        serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
    String aggInterface = "I" + contract + "Handler";
    String fakeClass = "Fake" + contract + "Handler";

    writer.writeXmlDocs(
        "Fake "
            + aggInterface
            + " returning canned responses: the output of each operation's first non-error"
            + " @examples entry when present, otherwise placeholder values synthesized from the"
            + " model. Responses are deterministic. Override an operation method in a subclass, or"
            + " register a real per-operation handler after this one, to replace individual"
            + " operations.",
        Map.of());
    writer.write("public class $L : $L", fakeClass, aggInterface);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (OperationShape op : ops) {
            if (!first) {
              writer.write("");
            }
            first = false;
            writeOperationMethod(op);
          }
          values.writePendingIterators(writer);
        });
  }

  // ---------------- operation methods ----------------

  private void writeOperationMethod(OperationShape op) {
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    writer.write("public virtual $L", operationSignature(op));
    if (!hasOutput) {
      writer.openBlock(
          "{", "}", () -> writer.write("return System.Threading.Tasks.Task.CompletedTask;"));
      return;
    }

    String expr = values.outputExpr(op);
    writer.openBlock(
        "{", "}", () -> writer.write("return System.Threading.Tasks.Task.FromResult($L);", expr));
  }

  /** Same delegate shape the handler interfaces declare; see ServerGenerator. */
  private String operationSignature(OperationShape op) {
    Model model = context.model();
    SymbolProvider sp = context.symbolProvider();
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<"
                + CSharpSymbolProvider.qualified(
                    sp.toSymbol(model.expectShape(op.getOutputShape())))
                + ">"
            : "System.Threading.Tasks.Task";
    String params =
        hasInput
            ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())))
                + " input, "
            : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }
}

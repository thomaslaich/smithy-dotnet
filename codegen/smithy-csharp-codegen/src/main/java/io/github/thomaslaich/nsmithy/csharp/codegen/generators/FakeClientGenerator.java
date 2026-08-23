/*
 * Fake client generator, opt-in via generateFakes. Emits:
 *   - `Fake{Service}Client : I{Service}Client` whose methods return canned responses synthesized
 *     by FakeValueSynthesizer, with no network call, serialization, or protocol involvement.
 *
 * Operation methods are virtual so a subclass can replace individual operations. Because no wire
 * protocol is involved, every operation responds, including event-stream operations the real
 * client rejects when no declared protocol supports them.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginationInfo;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class FakeClientGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final FakeValueSynthesizer values;

  public FakeClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.values = new FakeValueSynthesizer(c, "fake client");
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

    String typeName = CSharpNaming.typeName(service.getId().getName()) + "Client";
    String interfaceName = "I" + typeName;
    String fakeClass = "Fake" + typeName;

    writer.writeXmlDocs(
        "Fake "
            + interfaceName
            + " returning canned responses without any network call: the output of each"
            + " operation's first non-error @examples entry when present, otherwise placeholder"
            + " values synthesized from the model. Responses are deterministic. Override an"
            + " operation method in a subclass to replace individual operations.",
        Map.of());
    writer.write("public class $L : $L", fakeClass, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (OperationShape op : ops) {
            writeOperationMethod(op);
            ClientGenerator.paginationInfo(context, service, op)
                .ifPresent(info -> writePaginatorMethods(op, info));
            writer.write("");
          }
          writer.write("public virtual void Dispose() { }");
          values.writePendingIterators(writer);
        });
  }

  // ---------------- operation methods ----------------

  private void writeOperationMethod(OperationShape op) {
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    writer.write("public virtual $L", ClientGenerator.operationSignature(context, op));
    if (!hasOutput) {
      writer.openBlock(
          "{", "}", () -> writer.write("return System.Threading.Tasks.Task.CompletedTask;"));
      return;
    }

    String expr = values.outputExpr(op);
    writer.openBlock(
        "{", "}", () -> writer.write("return System.Threading.Tasks.Task.FromResult($L);", expr));
  }

  /**
   * The fake paginators yield a single page. The fake output's continuation token may be non-null,
   * so following it the way the real paginators do would never terminate. Pages flow through the
   * virtual unary method, so overriding it also changes what the paginators yield.
   */
  private void writePaginatorMethods(OperationShape op, PaginationInfo info) {
    String opName = CSharpNaming.typeName(op.getId().getName());

    writer.write("");
    writer.write(
        "public virtual async $L",
        ClientGenerator.withEnumeratorCancellation(
            ClientGenerator.paginatorPagesSignature(context, op)));
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "yield return await $LAsync(input, cancellationToken).ConfigureAwait(false);",
                opName));

    ClientGenerator.paginatorItemsSignature(context, info)
        .ifPresent(
            signature -> {
              String itemsExpr = ClientGenerator.memberPathExpr("page", info.getItemsMemberPath());
              writer.write("");
              writer.write(
                  "public virtual async $L", ClientGenerator.withEnumeratorCancellation(signature));
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    writer.write(
                        "await foreach (var page in $LPagesAsync(input,"
                            + " cancellationToken).ConfigureAwait(false))",
                        opName);
                    writer.openBlock(
                        "{",
                        "}",
                        () -> {
                          writer.write("var items = $L;", itemsExpr);
                          writer.write("if (items is null)");
                          writer.openBlock("{", "}", () -> writer.write("continue;"));
                          writer.write("foreach (var item in items.Values)");
                          writer.openBlock("{", "}", () -> writer.write("yield return item;"));
                        });
                  });
            });
  }
}

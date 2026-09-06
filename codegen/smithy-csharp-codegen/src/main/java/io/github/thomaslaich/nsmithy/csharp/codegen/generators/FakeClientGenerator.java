/*
 * Fake client generator, opt-in via generateFakes. Emits:
 *   - `Fake{Service}Client : I{Service}Client` whose methods return canned responses synthesized
 *     by FakeValueSynthesizer, with no network call, serialization, or protocol involvement.
 *     Operations with multiple @examples entries (or error examples) match the incoming input
 *     against the example inputs via FakeExampleMatcher to pick the response.
 *
 * Operation methods are virtual so a subclass can replace individual operations. Because no wire
 * protocol is involved, every operation responds, including event-stream operations the real
 * client rejects when no declared protocol supports them.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
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
  private final FakeExampleMatcher matcher;

  public FakeClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.values = new FakeValueSynthesizer(c, w, "fake client");
    this.matcher = new FakeExampleMatcher(c, w, values);
  }

  @Override
  public void run() {
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> ops =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());

    String typeName = CSharpNaming.typeName(service.getId().getName()) + "Client";
    String interfaceName = "I" + typeName;
    String fakeClass = "Fake" + typeName;

    writer.writeXmlDocs(
        "Fake "
            + interfaceName
            + " returning canned responses without any network call. When an operation has"
            + " multiple @examples entries the input is matched against the example inputs in"
            + " model order (members absent from an example are wildcards) and the first match"
            + " decides the response; a matched error example throws the modeled error. Otherwise"
            + " the first non-error @examples output is returned when present, placeholder values"
            + " synthesized from the model otherwise. Responses are deterministic. Override an"
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
          matcher.writePendingMatchers(writer);
          values.writePendingIterators(writer);
        });
  }

  // ---------------- operation methods ----------------

  private void writeOperationMethod(OperationShape op) {
    writer.write("public virtual $L", ClientGenerator.operationSignature(writer, context, op));
    writer.openBlock("{", "}", () -> matcher.writeOperationBody(writer, op));
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
            writer, ClientGenerator.paginatorPagesSignature(writer, context, op)));
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "yield return await $LAsync(input, cancellationToken).ConfigureAwait(false);",
                opName));

    ClientGenerator.paginatorItemsSignature(writer, context, info)
        .ifPresent(
            signature -> {
              String itemsExpr = ClientGenerator.memberPathExpr("page", info.getItemsMemberPath());
              writer.write("");
              writer.write(
                  "public virtual async $L",
                  ClientGenerator.withEnumeratorCancellation(writer, signature));
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

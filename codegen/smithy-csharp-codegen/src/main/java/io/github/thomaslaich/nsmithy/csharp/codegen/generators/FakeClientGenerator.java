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
    writer.pushState();
    try {
      writer.putContext("fakeClass", fakeClass);
      writer.putContext("interfaceName", interfaceName);
      writer.putContext(
          "operations",
          writer.consumer(
              w -> {
                for (OperationShape op : ops) {
                  writeOperationMethod(op);
                  ClientGenerator.paginationInfo(context, service, op)
                      .ifPresent(info -> writePaginatorMethods(op, info));
                  w.write("");
                }
              }));
      writer.putContext(
          "helpers",
          writer.consumer(
              w -> {
                w.write("public virtual void Dispose() { }");
                matcher.writePendingMatchers(w);
                values.writePendingIterators(w);
              }));
      writer.write(
          """
          public class ${fakeClass:L} : ${interfaceName:L}
          {
              ${operations:C|}
              ${helpers:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  // ---------------- operation methods ----------------

  private void writeOperationMethod(OperationShape op) {
    writer.pushState();
    try {
      writer.putContext("signature", ClientGenerator.operationSignature(writer, context, op));
      writer.putContext("body", writer.consumer(w -> matcher.writeOperationBody(w, op)));
      writer.write(
          """
          public virtual ${signature:L}
          {
              ${body:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * The fake paginators yield a single page. The fake output's continuation token may be non-null,
   * so following it the way the real paginators do would never terminate. Pages flow through the
   * virtual unary method, so overriding it also changes what the paginators yield.
   */
  private void writePaginatorMethods(OperationShape op, PaginationInfo info) {
    writer.pushState();
    try {
      writer.putContext("operation", CSharpNaming.typeName(op.getId().getName()));
      writer.putContext(
          "pagesSignature",
          ClientGenerator.withEnumeratorCancellation(
              writer, ClientGenerator.paginatorPagesSignature(writer, context, op)));
      writer.write("");
      writer.write(
          """
          public virtual async ${pagesSignature:L}
          {
              yield return await ${operation:L}Async(input, cancellationToken).ConfigureAwait(false);
          }
          """);
      ClientGenerator.paginatorItemsSignature(writer, context, info)
          .ifPresent(
              signature -> {
                writer.putContext(
                    "itemsSignature",
                    ClientGenerator.withEnumeratorCancellation(writer, signature));
                writer.putContext(
                    "items", ClientGenerator.memberPathExpr("page", info.getItemsMemberPath()));
                writer.write("");
                writer.write(
                    """
                    public virtual async ${itemsSignature:L}
                    {
                        await foreach (var page in ${operation:L}PagesAsync(input, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            var items = ${items:L};
                            if (items is null)
                            {
                                continue;
                            }
                            foreach (var item in items.Values)
                            {
                                yield return item;
                            }
                        }
                    }
                    """);
              });
    } finally {
      writer.popState();
    }
  }
}

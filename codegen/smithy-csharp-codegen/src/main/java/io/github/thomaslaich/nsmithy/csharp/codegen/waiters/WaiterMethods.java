/*
 * The waiter methods of the generated client interface, client, and fake client.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.waiters;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ClientGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.LinkedHashMap;
import java.util.Map;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;
import software.amazon.smithy.waiters.WaitableTrait;
import software.amazon.smithy.waiters.Waiter;

@SmithyInternalApi
public final class WaiterMethods {

  private WaiterMethods() {}

  /** The waiters of {@code op}, by name. Event-stream operations have none. */
  public static Map<String, Waiter> waiters(GenerationContext context, OperationShape op) {
    if (ClientGenerator.isEventStreamOperation(context.model(), op)) {
      return Map.of();
    }
    return op.getTrait(WaitableTrait.class)
        .map(trait -> (Map<String, Waiter>) new LinkedHashMap<>(trait.getWaiters()))
        .orElse(Map.of());
  }

  /** Whether any operation of {@code service} has a waiter. */
  public static boolean hasWaiters(GenerationContext context, ServiceShape service) {
    return TopDownIndex.of(context.model()).getContainedOperations(service).stream()
        .anyMatch(op -> !waiters(context, op).isEmpty());
  }

  /** The name of the static class holding a service's acceptor tables. */
  public static String waitersClass(ServiceShape service) {
    return CSharpNaming.typeName(service.getId().getName()) + "Waiters";
  }

  /** Writes the interface declarations of {@code op}'s waiter methods. */
  public static void writeDeclarations(
      CSharpWriter writer, GenerationContext context, OperationShape op) {
    for (Map.Entry<String, Waiter> waiter : waiters(context, op).entrySet()) {
      writeDocs(writer, waiter.getValue(), false);
      writer.write("$L;", maxWaitTimeSignature(writer, context, op, waiter.getKey()));
      writeDocs(writer, waiter.getValue(), true);
      writer.write("$L;", optionsSignature(writer, context, op, waiter.getKey()));
    }
  }

  /**
   * Writes {@code op}'s waiter methods. Each attempt calls the class's own operation method, so a
   * fake client waits over its canned responses.
   */
  public static void writeImplementations(
      CSharpWriter writer,
      GenerationContext context,
      ServiceShape service,
      OperationShape op,
      String modifiers) {
    for (Map.Entry<String, Waiter> entry : waiters(context, op).entrySet()) {
      Waiter waiter = entry.getValue();
      String name = CSharpNaming.typeName(entry.getKey());
      writer.pushState();
      try {
        writer.putContext("modifiers", modifiers);
        writer.putContext(
            "maxWaitTimeSignature", maxWaitTimeSignature(writer, context, op, entry.getKey()));
        writer.putContext(
            "optionsSignature", optionsSignature(writer, context, op, entry.getKey()));
        writer.putContext("options", RuntimeTypes.SMITHY_WAITER_OPTIONS);
        writer.putContext("waiter", RuntimeTypes.SMITHY_WAITER);
        writer.putContext("timeSpan", RuntimeTypes.TIME_SPAN);
        writer.putContext("name", name);
        writer.putContext("modeledName", CSharpNaming.formatString(entry.getKey()));
        writer.putContext("operation", CSharpNaming.typeName(op.getId().getName()));
        writer.putContext("acceptors", waitersClass(service) + "." + name);
        writer.putContext("minDelay", waiter.getMinDelay());
        writer.putContext("maxDelay", waiter.getMaxDelay());
        writeDocs(writer, waiter, false);
        writer.write(
            """
            ${modifiers:L}${maxWaitTimeSignature:L} =>
                WaitUntil${name:L}Async(input, new ${options:T}(maxWaitTime), cancellationToken);
            """);
        writeDocs(writer, waiter, true);
        writer.write(
            """
            ${modifiers:L}${optionsSignature:L} =>
                ${waiter:T}.WaitAsync(
                    ${modeledName:L},
                    ${operation:L}Async,
                    input,
                    ${acceptors:L},
                    ${timeSpan:T}.FromSeconds(${minDelay:L}),
                    ${timeSpan:T}.FromSeconds(${maxDelay:L}),
                    options,
                    cancellationToken);
            """);
      } finally {
        writer.popState();
      }
    }
  }

  private static String maxWaitTimeSignature(
      CSharpWriter writer, GenerationContext context, OperationShape op, String waiter) {
    return signature(
        writer, context, op, waiter, writer.format("$T maxWaitTime", RuntimeTypes.TIME_SPAN));
  }

  private static String optionsSignature(
      CSharpWriter writer, GenerationContext context, OperationShape op, String waiter) {
    return signature(
        writer,
        context,
        op,
        waiter,
        writer.format("$T options", RuntimeTypes.SMITHY_WAITER_OPTIONS));
  }

  private static String signature(
      CSharpWriter writer,
      GenerationContext context,
      OperationShape op,
      String waiter,
      String limit) {
    Model model = context.model();
    String input =
        writer.typeName(context.symbolProvider().toSymbol(model.expectShape(op.getInputShape())));
    String output =
        writer.typeName(context.symbolProvider().toSymbol(model.expectShape(op.getOutputShape())));
    return writer.format(
        "$T<$T<$L>> WaitUntil$LAsync($L input, $L, $T cancellationToken = default)",
        RuntimeTypes.TASK,
        RuntimeTypes.SMITHY_WAITER_RESULT,
        output,
        CSharpNaming.typeName(waiter),
        input,
        limit,
        RuntimeTypes.CANCELLATION_TOKEN);
  }

  private static void writeDocs(CSharpWriter writer, Waiter waiter, boolean options) {
    Map<String, String> parameters = new LinkedHashMap<>();
    parameters.put("input", "The operation input every attempt sends.");
    if (options) {
      parameters.put(
          "options",
          "The maximum wait time, and overrides of the modeled delays between attempts.");
    } else {
      parameters.put("maxWaitTime", "The longest the waiter polls before it times out.");
    }
    parameters.put("cancellationToken", "Cancels the current attempt or delay.");
    writer.writeXmlDocs(waiter.getDocumentation().orElse(null), parameters);
  }
}

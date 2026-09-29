package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.model.shapes.ServiceShape;

/** Connects generated definitions to reusable runtime hosting. */
public final class KafkaDependencyInjectionGenerator implements Runnable {
  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public KafkaDependencyInjectionGenerator(
      GenerationContext context, CSharpWriter writer, ServiceShape service) {
    this.context = context;
    this.writer = writer;
    this.service = service;
  }

  @Override
  public void run() {
    String svc = CSharpNaming.typeName(service.getId().getName());
    var produces = KafkaBindings.produces(context.model(), context.symbolProvider(), service);
    var consumes = KafkaBindings.consumes(context.model(), context.symbolProvider(), service);
    writer.addImport("Microsoft.Extensions.DependencyInjection");
    writer.addImport("Microsoft.Extensions.DependencyInjection.Extensions");
    writer.addImport("NSmithy.Messaging.Kafka");
    writer.write("public static class $LMessagingExtensions", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (!produces.isEmpty()) {
            writeRole(svc, "Client");
            writeConsumer(
                svc, "Command", produces.stream().map(KafkaBindings.Produce::opName).toList());
          }
          if (!consumes.isEmpty()) {
            writeRole(svc, "EventPublisher");
            writeConsumer(
                svc, "Event", consumes.stream().map(KafkaBindings.Consume::opName).toList());
          }
        });
  }

  private void writeRole(String svc, String role) {
    writer.write(
        "public static IServiceCollection Add$L$L(this IServiceCollection services)", svc, role);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("services.TryAddSingleton<I$L$L, $L$L>();", svc, role, svc, role);
          writer.write("return services;");
        });
  }

  private void writeConsumer(String svc, String role, List<String> operations) {
    writer.write(
        "public static IServiceCollection Add$L$LConsumer(this IServiceCollection services)",
        svc,
        role);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "return services.AddKafkaMessageConsumer($L);",
                operations.stream()
                    .map(op -> svc + "Messaging." + op + "Receive")
                    .collect(Collectors.joining(", "))));
  }
}

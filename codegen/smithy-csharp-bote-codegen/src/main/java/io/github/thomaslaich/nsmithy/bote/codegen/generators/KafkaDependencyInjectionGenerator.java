/** Generates opt-in hosting registrations with one dependency-injection scope per message. */
package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.bote.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class KafkaDependencyInjectionGenerator implements Runnable {

  private static final String MS_EXT_DI = "Microsoft.Extensions.DependencyInjection";
  private static final String MS_EXT_HOSTING = "Microsoft.Extensions.Hosting";

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public KafkaDependencyInjectionGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    Model model = context.model();
    List<KafkaBindings.Produce> produces =
        KafkaBindings.produces(model, context.symbolProvider(), service);
    List<KafkaBindings.Consume> consumes =
        KafkaBindings.consumes(model, context.symbolProvider(), service);

    if (produces.isEmpty() && consumes.isEmpty()) return;

    writer.addImport(RuntimeTypes.CONFLUENT_KAFKA);
    writer.addImport(MS_EXT_DI);
    writer.addImport(MS_EXT_HOSTING);

    String svc = CSharpNaming.typeName(service.getId().getName());

    writeExtensions(svc, produces, consumes);

    if (!produces.isEmpty()) {
      writer.write("");
      writeScopedHandler(
          "Scoped" + svc + "CommandHandler",
          "I" + svc + "CommandHandler",
          () -> {
            boolean first = true;
            for (KafkaBindings.Produce produce : produces) {
              if (!first) writer.write("");
              first = false;
              writeScopedHandlerMethod(
                  "I" + svc + "CommandHandler",
                  "Handle" + produce.opName() + "Async",
                  produce.commandType(),
                  "command");
            }
          });
      writer.write("");
      writeConsumerService(svc + "CommandConsumer");
    }

    if (!consumes.isEmpty()) {
      writer.write("");
      writeScopedHandler(
          "Scoped" + svc + "EventHandler",
          "I" + svc + "EventHandler",
          () -> {
            boolean first = true;
            for (KafkaBindings.Consume consume : consumes) {
              for (MemberShape member : consume.members()) {
                if (!first) writer.write("");
                first = false;
                writeScopedHandlerMethod(
                    "I" + svc + "EventHandler",
                    "Handle" + CSharpNaming.typeName(member.getMemberName()) + "Async",
                    qualified(model, member),
                    "message");
              }
            }
          });
      writer.write("");
      writeConsumerService(svc + "EventConsumer");
    }
  }

  // IServiceCollection extensions

  private void writeExtensions(
      String svc, List<KafkaBindings.Produce> produces, List<KafkaBindings.Consume> consumes) {
    writer.write("public static class $LKafkaServiceCollectionExtensions", svc);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "/// <summary>Registers <see cref=\"$LProducer\"/> as a singleton. Confluent"
                  + " producers are thread-safe and meant to be shared.</summary>",
              svc);
          writer.write("public static IServiceCollection Add$LProducer(", svc);
          writer.write("    this IServiceCollection services, ProducerConfig config)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(services);");
                writer.write("System.ArgumentNullException.ThrowIfNull(config);");
                writer.write("services.AddSingleton(_ => new $LProducer(config));", svc);
                writer.write("return services;");
              });

          if (!produces.isEmpty()) {
            writer.write("");
            writeAddConsumer(svc, "CommandConsumer", "I" + svc + "CommandHandler");
          }

          if (!consumes.isEmpty()) {
            writer.write("");
            writeAddConsumer(svc, "EventConsumer", "I" + svc + "EventHandler");
          }
        });
  }

  private void writeAddConsumer(String svc, String consumerKind, String ifaceName) {
    String consumerName = svc + consumerKind;
    String adapterName = "Scoped" + ifaceName.substring(1);
    writer.write(
        "/// <summary>Runs <see cref=\"$L\"/> for the host lifetime. Register an"
            + " <see cref=\"$L\"/> implementation (any lifetime); it is resolved in a new"
            + " service scope per message.</summary>",
        consumerName,
        ifaceName);
    writer.write("public static IServiceCollection Add$L(", consumerName);
    writer.write("    this IServiceCollection services, ConsumerConfig config)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(services);");
          writer.write("System.ArgumentNullException.ThrowIfNull(config);");
          writer.write("services.AddHostedService(provider => new $LService(", consumerName);
          writer.write("    new $L(", consumerName);
          writer.write("        config,");
          writer.write(
              "        new $L(provider.GetRequiredService<IServiceScopeFactory>()))));",
              adapterName);
          writer.write("return services;");
        });
  }

  // Scoped handler adapters
  private void writeScopedHandler(String adapterName, String ifaceName, Runnable methods) {
    writer.write("internal sealed class $L : $L", adapterName, ifaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly IServiceScopeFactory _scopes;");
          writer.write("");
          writer.write("public $L(IServiceScopeFactory scopes)", adapterName);
          writer.openBlock("{", "}", () -> writer.write("_scopes = scopes;"));
          writer.write("");
          methods.run();
        });
  }

  private void writeScopedHandlerMethod(
      String ifaceName, String methodName, String payloadType, String paramName) {
    writer.write(
        "public async System.Threading.Tasks.Task $L($L $L,"
            + " System.Threading.CancellationToken cancellationToken = default)",
        methodName,
        payloadType,
        paramName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("await using var scope = _scopes.CreateAsyncScope();");
          writer.write("await scope.ServiceProvider");
          writer.write("    .GetRequiredService<$L>()", ifaceName);
          writer.write("    .$L($L, cancellationToken);", methodName, paramName);
        });
  }

  // Hosted services
  private void writeConsumerService(String consumerName) {
    String serviceName = consumerName + "Service";
    writer.write("internal sealed class $L : BackgroundService", serviceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly $L _consumer;", consumerName);
          writer.write("");
          writer.write("public $L($L consumer)", serviceName, consumerName);
          writer.openBlock("{", "}", () -> writer.write("_consumer = consumer;"));
          writer.write("");
          writer.write(
              "protected override async System.Threading.Tasks.Task ExecuteAsync("
                  + "System.Threading.CancellationToken stoppingToken)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("try");
                writer.openBlock(
                    "{", "}", () -> writer.write("await _consumer.RunAsync(stoppingToken);"));
                writer.write(
                    "catch (System.OperationCanceledException) when"
                        + " (stoppingToken.IsCancellationRequested) { }");
              });
          writer.write("");
          writer.write("public override void Dispose()");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("_consumer.DisposeAsync().AsTask().GetAwaiter().GetResult();");
                writer.write("base.Dispose();");
              });
        });
  }

  // Helpers

  private String qualified(Model model, MemberShape member) {
    return CSharpSymbolProvider.qualified(
        context.symbolProvider().toSymbol(model.expectShape(member.getTarget())));
  }
}

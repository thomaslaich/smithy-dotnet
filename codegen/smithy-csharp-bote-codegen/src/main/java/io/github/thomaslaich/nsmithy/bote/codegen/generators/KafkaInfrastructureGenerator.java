package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import io.github.thomaslaich.nsmithy.bote.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Map;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

/** Emits the modeled desired state for Kafka topics owned by a service. */
@SmithyInternalApi
public final class KafkaInfrastructureGenerator implements Runnable {
  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public KafkaInfrastructureGenerator(
      GenerationContext context, CSharpWriter writer, ServiceShape service) {
    this.context = context;
    this.writer = writer;
    this.service = service;
  }

  @Override
  public void run() {
    List<KafkaBindings.TopicConfiguration> topics =
        KafkaBindings.topicConfigurations(context.model(), service);
    if (topics.isEmpty()) return;

    writer.addImport("System.Collections.Generic");
    String serviceName = CSharpNaming.typeName(service.getId().getName());
    writer.write("public static class $LKafkaInfrastructure", serviceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public sealed record Topic(");
          writer.indent();
          writer.write("string Name,");
          writer.write("int? Partitions,");
          writer.write("short? ReplicationFactor,");
          writer.write("IReadOnlyDictionary<string, string> Configuration");
          writer.dedent();
          writer.write(");");
          writer.write("");
          writer.write("public static IReadOnlyList<Topic> Topics { get; } =");
          writer.write("[");
          writer.indent();
          for (KafkaBindings.TopicConfiguration topic : topics) {
            writer.write("new Topic(");
            writer.indent();
            writer.write("Name: $L,", CSharpNaming.formatString(topic.topic()));
            writer.write(
                "Partitions: $L,",
                topic.partitions() == null ? "null" : topic.partitions().toString());
            writer.write(
                "ReplicationFactor: $L,",
                topic.replicationFactor() == null ? "null" : topic.replicationFactor().toString());
            writer.write("Configuration: new Dictionary<string, string>");
            writer.write("{");
            writer.indent();
            for (Map.Entry<String, String> entry : topic.configuration().entrySet()) {
              writer.write(
                  "[$L] = $L,",
                  CSharpNaming.formatString(entry.getKey()),
                  CSharpNaming.formatString(entry.getValue()));
            }
            writer.dedent();
            writer.write("}");
            writer.dedent();
            writer.write("),");
          }
          writer.dedent();
          writer.write("];");
        });
  }
}

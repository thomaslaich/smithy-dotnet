package io.github.thomaslaich.nsmithy.bote.codegen;

import io.github.thomaslaich.nsmithy.bote.codegen.generators.KafkaDependencyInjectionGenerator;
import io.github.thomaslaich.nsmithy.bote.codegen.generators.KafkaGenerator;
import io.github.thomaslaich.nsmithy.bote.codegen.generators.KafkaInfrastructureGenerator;
import io.github.thomaslaich.nsmithy.bote.codegen.generators.RedisGenerator;
import io.github.thomaslaich.nsmithy.bote.codegen.support.KafkaBindings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpServiceGenerator;
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ServiceShape;

public final class BoteCSharpIntegration implements CSharpIntegration {
  @Override
  public Optional<CSharpServiceGenerator> serviceGenerator(
      GenerationContext context, ServiceShape service) {
    if (service.hasTrait(TraitIds.KAFKA_JSON)) {
      return Optional.of(this::generateKafka);
    }
    if (service.hasTrait(TraitIds.KAFKA_AVRO) || service.hasTrait(TraitIds.KAFKA_PROTOBUF)) {
      return Optional.of(
          (ctx, shape) -> {
            throw new CodegenException(
                "Bote C# codegen currently supports @kafkaJson, not the protocol on "
                    + shape.getId());
          });
    }
    if (service.hasTrait(TraitIds.REDIS_STREAMS_JSON)) {
      return Optional.of(
          (ctx, shape) -> generateRedis(ctx, shape, RedisGenerator.Kind.STREAMS, "RedisStreams"));
    }
    if (service.hasTrait(TraitIds.REDIS_PUB_SUB_JSON)) {
      return Optional.of(
          (ctx, shape) -> generateRedis(ctx, shape, RedisGenerator.Kind.PUB_SUB, "RedisPubSub"));
    }
    return Optional.empty();
  }

  private void generateKafka(GenerationContext context, ServiceShape service) {
    String namespace = context.settings().csharpNamespace(service.getId().getNamespace());
    String typeName = CSharpNaming.typeName(service.getId().getName());
    String directory = namespace.replace('.', '/');

    if (context.settings().generateClient() || context.settings().generateServer()) {
      context
          .writerDelegator()
          .useFileWriter(
              directory + "/" + typeName + "Kafka.g.cs",
              namespace,
              writer -> new KafkaGenerator(context, writer, service).run());
    }

    if (!KafkaBindings.topicConfigurations(context.model(), service).isEmpty()) {
      context
          .writerDelegator()
          .useFileWriter(
              directory + "/" + typeName + "Kafka.Infrastructure.g.cs",
              namespace,
              writer -> new KafkaInfrastructureGenerator(context, writer, service).run());
    }

    if (context.settings().generateDependencyInjection()
        && (context.settings().generateClient() || context.settings().generateServer())) {
      context
          .writerDelegator()
          .useFileWriter(
              directory + "/" + typeName + "Kafka.DependencyInjection.g.cs",
              namespace,
              writer -> new KafkaDependencyInjectionGenerator(context, writer, service).run());
    }
  }

  private void generateRedis(
      GenerationContext context,
      ServiceShape service,
      RedisGenerator.Kind kind,
      String fileSuffix) {
    String namespace = context.settings().csharpNamespace(service.getId().getNamespace());
    String typeName = CSharpNaming.typeName(service.getId().getName());
    String directory = namespace.replace('.', '/');
    context
        .writerDelegator()
        .useFileWriter(
            directory + "/" + typeName + fileSuffix + ".g.cs",
            namespace,
            writer -> new RedisGenerator(context, writer, service, kind).run());
  }
}

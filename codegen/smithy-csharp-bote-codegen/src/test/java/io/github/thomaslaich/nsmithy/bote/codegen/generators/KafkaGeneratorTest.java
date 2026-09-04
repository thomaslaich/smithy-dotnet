package io.github.thomaslaich.nsmithy.bote.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Files;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class KafkaGeneratorTest {

  private static final String MODEL =
      """
      $version: "2"

      namespace example.messaging

      use bote#command
      use bote#event
      use bote#kafkaConsume
      use bote#kafkaHeader
      use bote#kafkaJson
      use bote#kafkaKey
      use bote#kafkaProduce
      use bote.infra#kafkaTopicConfig

      @kafkaJson
      service Device {
          version: "1"
          operations: [Dim, Watch]
      }

      @kafkaProduce(topic: "device.commands")
      operation Dim {
          input: DimCommand
      }

      @command
      structure DimCommand {
          @kafkaKey
          deviceId: String

          @kafkaHeader(name: "trace-id")
          traceId: String

          percentage: Integer
      }

      @kafkaConsume(topic: "device.events")
      operation Watch {
          output := {
              events: DeviceEvents
          }
      }

      @streaming
      union DeviceEvents {
          measured: Measured
      }

      @event
      structure Measured {
          lumens: Integer
      }

      apply example.messaging#Dim @kafkaTopicConfig(
          partitions: 3
          replicationFactor: 2
          retentionMs: 86400000
      )
      """;

  @TempDir java.nio.file.Path tempDir;

  @Test
  void generatesProducerAndOwnerAndClientConsumers() throws Exception {
    String generated = renderKafka();

    assertTrue(generated.contains("public sealed class DeviceProducer"), generated);
    assertTrue(generated.contains("public System.Threading.Tasks.Task DimAsync("), generated);
    assertTrue(
        generated.contains("public System.Threading.Tasks.Task PublishMeasuredAsync("), generated);
    assertTrue(generated.contains("public sealed class DeviceCommandConsumer"), generated);
    assertTrue(generated.contains("public sealed class DeviceEventConsumer"), generated);
    assertTrue(generated.contains("var key = command.DeviceId;"), generated);
    assertTrue(
        generated.contains("headers.Add(\"trace-id\", Encoding.UTF8.GetBytes(traceId));"),
        generated);
  }

  @Test
  void generatesTopicInfrastructureFromTheDeploymentOverlay() throws Exception {
    RenderContext rendered = context();
    var writer = new CSharpWriter("Example.Example.Messaging");

    new KafkaInfrastructureGenerator(rendered.context(), writer, rendered.service()).run();

    String generated = writer.toString();
    assertTrue(generated.contains("public static class DeviceKafkaInfrastructure"), generated);
    assertTrue(generated.contains("Name: \"device.commands\""), generated);
    assertTrue(generated.contains("Partitions: 3"), generated);
    assertTrue(generated.contains("ReplicationFactor: 2"), generated);
    assertTrue(generated.contains("[\"retention.ms\"] = \"86400000\""), generated);
  }

  @Test
  void storesOffsetsOnlyAfterSuccessfulDispatch() throws Exception {
    String generated = renderKafka();

    assertEquals(2, occurrences(generated, "consumerConfig[\"enable.auto.commit\"] = \"true\";"));
    assertEquals(
        2, occurrences(generated, "consumerConfig[\"enable.auto.offset.store\"] = \"false\";"));
    assertEquals(2, occurrences(generated, "_consumer.StoreOffset(result);"));

    int commandConsumer = generated.indexOf("public sealed class DeviceCommandConsumer");
    int eventConsumer = generated.indexOf("public sealed class DeviceEventConsumer");
    assertDispatchBeforeOffsetStore(generated.substring(commandConsumer, eventConsumer));
    assertDispatchBeforeOffsetStore(generated.substring(eventConsumer));
  }

  private void assertDispatchBeforeOffsetStore(String consumer) {
    int dispatch = consumer.indexOf("await DispatchAsync(result, cancellationToken);");
    int store = consumer.indexOf("_consumer.StoreOffset(result);");
    assertTrue(dispatch >= 0, consumer);
    assertTrue(store > dispatch, consumer);
  }

  private String renderKafka() throws Exception {
    RenderContext rendered = context();
    var writer = new CSharpWriter("Example.Example.Messaging");

    new KafkaGenerator(rendered.context(), writer, rendered.service()).run();

    return writer.toString();
  }

  private RenderContext context() throws Exception {
    Model model =
        Model.assembler()
            .discoverModels(getClass().getClassLoader())
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.messaging#Device"))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(Files.createDirectory(tempDir.resolve("manifest")));
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbolProvider)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider))
            .build();
    var service = model.expectShape(ShapeId.from("example.messaging#Device"), ServiceShape.class);
    return new RenderContext(context, service);
  }

  private record RenderContext(GenerationContext context, ServiceShape service) {}

  private static int occurrences(String value, String fragment) {
    int count = 0;
    int offset = 0;
    while ((offset = value.indexOf(fragment, offset)) >= 0) {
      count++;
      offset += fragment.length();
    }
    return count;
  }
}

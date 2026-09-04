package io.github.thomaslaich.nsmithy.bote.codegen.generators;

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

final class RedisGeneratorTest {

  private static final String MODEL =
      """
      $version: "2"

      namespace example.messaging

      use bote#command
      use bote#event
      use bote#redisStreamAdd
      use bote#redisStreamRead
      use bote#redisStreamsJson

      @redisStreamsJson
      service Chat {
          version: "1"
          operations: [Post, Watch]
      }

      @redisStreamAdd(stream: "chat.commands", maxLen: 1000)
      operation Post {
          input: PostCommand
      }

      @command
      structure PostCommand {
          text: String
      }

      @redisStreamRead(stream: "chat.events", maxLen: 1000)
      operation Watch {
          output := {
              events: ChatEvents
          }
      }

      @streaming
      union ChatEvents {
          posted: Posted
      }

      @event
      structure Posted {
          text: String
      }
      """;

  @TempDir java.nio.file.Path tempDir;

  @Test
  void generatesStreamsClientAndOwnerConsumer() throws Exception {
    String generated = renderRedis();

    assertTrue(generated.contains("public sealed class ChatRedisStreams"), generated);
    assertTrue(
        generated.contains("public async System.Threading.Tasks.Task PostAsync("), generated);
    assertTrue(generated.contains("public sealed class ChatRedisStreamsConsumer"), generated);
    assertTrue(generated.contains("public interface IChatRedisStreamsHandler"), generated);
    assertTrue(generated.contains("StreamAcknowledgeAsync"), generated);
  }

  private String renderRedis() throws Exception {
    Model model =
        Model.assembler()
            .discoverModels(getClass().getClassLoader())
            .addUnparsedModel("model.smithy", MODEL)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.messaging#Chat"))
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
    var writer = new CSharpWriter("Example.Example.Messaging");
    var service = model.expectShape(ShapeId.from("example.messaging#Chat"), ServiceShape.class);

    new RedisGenerator(context, writer, service, RedisGenerator.Kind.STREAMS).run();

    return writer.toString();
  }
}

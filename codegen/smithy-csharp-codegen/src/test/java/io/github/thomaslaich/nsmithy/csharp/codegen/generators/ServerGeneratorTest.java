package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
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
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class ServerGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.streaming

      use alloy.proto#grpc
      use smithy.api#streaming

      @grpc
      service StreamingService {
          version: "1"
          operations: [Watch, Upload, Chat]
      }

      operation Watch {
          input := {}
          output := {
              events: ChatEvent
          }
      }

      operation Upload {
          input := {
              events: ChatEvent
          }
          output := {}
      }

      operation Chat {
          input := {
              events: ChatEvent
          }
          output := {
              events: ChatEvent
          }
      }

      @streaming
      union ChatEvent {
          message: MessageEvent
      }

      structure MessageEvent {
          text: String
      }
      """;

  private static final String STREAMING_SIBLING_MODEL =
      """
      $version: "2"

      namespace example.streaming

      use alloy.proto#grpc
      use smithy.api#streaming

      @grpc
      service StreamingService {
          version: "1"
          operations: [Chat]
      }

      operation Chat {
          input := {
              room: String
              events: ChatEvent
          }
          output := {
              events: ChatEvent
          }
      }

      @streaming
      union ChatEvent {
          message: MessageEvent
      }

      structure MessageEvent {
          text: String
      }
      """;

  private static final String PROTOCOL_TRAITS =
      """
      $version: "2"

      namespace alloy.proto

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure grpc {}
      """;

  private static final String REST_PROTOCOL_TRAITS =
      """
      $version: "2"

      namespace aws.protocols

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure restJson1 {}
      """;

  private static final String REST_STREAMING_MODEL =
      """
      $version: "2"

      namespace example.reststreaming

      use aws.protocols#restJson1
      use smithy.api#streaming

      @restJson1
      service StreamingService {
          version: "1"
          operations: [Watch, Upload]
      }

      @http(method: "GET", uri: "/watch")
      operation Watch {
          input := {}
          output := {
              @httpPayload
              events: ChatEvent
          }
      }

      @http(method: "POST", uri: "/upload")
      operation Upload {
          input := {
              @httpPayload
              events: ChatEvent
          }
          output := {}
      }

      @streaming
      union ChatEvent {
          message: MessageEvent
      }

      structure MessageEvent {
          text: String
      }
      """;

  @Test
  void streamingGrpcServerUsesAsyncEnumerableHandlersAndStreamingWriter() throws Exception {
    String generated = renderServer();

    assertTrue(
        generated.contains(
            "System.Threading.Tasks.Task<Example.Example.Streaming.WatchOutput>"
                + " WatchAsync(Example.Example.Streaming.WatchInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "System.Threading.Tasks.Task<Example.Example.Streaming.UploadOutput>"
                + " UploadAsync(Example.Example.Streaming.UploadInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "System.Threading.Tasks.Task<Example.Example.Streaming.ChatOutput>"
                + " ChatAsync(Example.Example.Streaming.ChatInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertFalse(generated.contains("IEventStreamServiceProtocol"));
    // Streaming endpoints delegate to the shared runtime path; only request-body streaming is
    // selected per operation.
    assertTrue(generated.contains("WatchProtocol, handler.WatchAsync, false"), generated);
    assertTrue(generated.contains("UploadProtocol, handler.UploadAsync, true"), generated);
    assertTrue(generated.contains("ChatProtocol, handler.ChatAsync, true"), generated);
    assertTrue(generated.contains("MapStreamingServiceGrpc"), generated);
  }

  @Test
  void grpcStreamingOperationsRejectSiblingMembers() {
    CodegenException ex =
        assertThrows(CodegenException.class, () -> renderServer(STREAMING_SIBLING_MODEL));

    assertTrue(
        ex.getMessage()
            .contains(
                "gRPC event-stream operation example.streaming#Chat input shape"
                    + " example.streaming#ChatInput must contain exactly one event-stream member"),
        ex.getMessage());
  }

  @Test
  void restJson1EventStreamOperationsMapRestRoutes() throws Exception {
    String generated =
        renderServer(
            REST_PROTOCOL_TRAITS,
            REST_STREAMING_MODEL,
            "example.reststreaming#StreamingService",
            "Example.RestStreaming");

    assertTrue(generated.contains("MapStreamingServiceRestJson1"), generated);
    assertTrue(generated.contains("endpoints.MapMethods(\"/watch\", [\"GET\"]"), generated);
    assertTrue(generated.contains("WatchProtocol, handler.WatchAsync, false"), generated);
    assertTrue(generated.contains("endpoints.MapMethods(\"/upload\", [\"POST\"]"), generated);
    assertTrue(generated.contains("UploadProtocol, handler.UploadAsync, true"), generated);
  }

  private String renderServer() throws Exception {
    return renderServer(MODEL);
  }

  private String renderServer(String modelText) throws Exception {
    return renderServer(
        PROTOCOL_TRAITS, modelText, "example.streaming#StreamingService", "Example.Streaming");
  }

  private String renderServer(
      String protocolTraits, String modelText, String serviceId, String writerNamespace)
      throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("protocol-traits.smithy", protocolTraits)
            .addUnparsedModel("model.smithy", modelText)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from(serviceId))
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
    var writer = new CSharpWriter(writerNamespace);
    var service = model.expectShape(ShapeId.from(serviceId), ServiceShape.class);

    new ServerGenerator(context, writer, service).run();

    return writer.toString();
  }
}

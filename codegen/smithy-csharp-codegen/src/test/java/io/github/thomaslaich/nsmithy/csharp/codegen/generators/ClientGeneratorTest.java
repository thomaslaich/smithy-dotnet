package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
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

final class ClientGeneratorTest {

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

  @Test
  void streamingOperationsUseAsyncEnumerableSignatures() throws Exception {
    String generated = renderClient();

    assertTrue(
        generated.contains(
            "System.Collections.Generic.IAsyncEnumerable<Example.Example.Streaming.ChatEvent>"
                + " WatchAsync(Example.Example.Streaming.WatchInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "System.Threading.Tasks.Task<Example.Example.Streaming.UploadOutput>"
                + " UploadAsync(System.Collections.Generic.IAsyncEnumerable<Example.Example.Streaming.ChatEvent>"
                + " input, System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "System.Collections.Generic.IAsyncEnumerable<Example.Example.Streaming.ChatEvent>"
                + " ChatAsync(System.Collections.Generic.IAsyncEnumerable<Example.Example.Streaming.ChatEvent>"
                + " input, System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertFalse(generated.contains("WatchProtocol = serviceProtocol.ForOperation"), generated);
    assertFalse(generated.contains("Streaming operations are not wired"), generated);
    assertTrue(
        generated.contains(
            "private readonly SmithyEventStreamOperationInvoker eventStreamInvoker;"));
  }

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

  private static final String AUTH_MODEL =
      """
      $version: "2"

      namespace example.auth

      use aws.protocols#restJson1

      @restJson1
      @httpBearerAuth
      @httpApiKeyAuth(name: "x-api-key", in: "header")
      service Secured {
          version: "1"
          operations: [ReadThing, AdminThing]
      }

      @http(method: "GET", uri: "/thing")
      operation ReadThing {
          input := {}
          output := {}
      }

      @auth([httpApiKeyAuth])
      @http(method: "GET", uri: "/admin")
      operation AdminThing {
          input := {}
          output := {}
      }
      """;

  @Test
  void operationBindingsCarryEffectiveAuthSchemes() throws Exception {
    String generated =
        renderClient(REST_PROTOCOL_TRAITS, AUTH_MODEL, "example.auth#Secured", "Example.Auth");

    // ReadThing inherits the service's effective schemes (alphabetical by shape id).
    assertTrue(
        generated.contains(
            "serviceProtocol.ForOperation(Example.Example.Auth.ReadThingSchema.Schema), new"
                + " string[] { \"smithy.api#httpApiKeyAuth\", \"smithy.api#httpBearerAuth\" });"),
        generated);
    // AdminThing's @auth trait overrides the service default.
    assertTrue(
        generated.contains(
            "serviceProtocol.ForOperation(Example.Example.Auth.AdminThingSchema.Schema), new"
                + " string[] { \"smithy.api#httpApiKeyAuth\" });"),
        generated);
  }

  @Test
  void endpointConstructorCopiesCallerConfig() throws Exception {
    String generated = renderClient();

    // The endpoint constructor must never mutate the caller's config instance.
    assertTrue(
        generated.contains(
            "var copy = config is null ? new StreamingServiceClientConfig() : new"
                + " StreamingServiceClientConfig(config);"),
        generated);
    assertTrue(
        generated.contains(
            "public StreamingServiceClientConfig(StreamingServiceClientConfig source) :"
                + " base(source) { }"),
        generated);
  }

  private String renderClient() throws Exception {
    return renderClient(
        PROTOCOL_TRAITS, MODEL, "example.streaming#StreamingService", "Example.Streaming");
  }

  private String renderClient(
      String protocolTraits, String serviceModel, String serviceId, String writerNamespace)
      throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel("protocol-traits.smithy", protocolTraits)
            .addUnparsedModel("model.smithy", serviceModel)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from(serviceId))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest =
        FileManifest.create(
            Files.createDirectory(tempDir.resolve("manifest-" + serviceId.replace('#', '-'))));
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

    new ClientGenerator(context, writer, service).run();

    return writer.toString();
  }
}

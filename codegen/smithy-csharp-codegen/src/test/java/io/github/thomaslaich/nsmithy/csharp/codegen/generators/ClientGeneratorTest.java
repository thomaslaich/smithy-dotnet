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

  @Test
  void streamingOperationsUseAsyncEnumerableSignatures() throws Exception {
    String generated = renderClient();

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
    assertFalse(generated.contains("ForOutputEventStreamOperation"), generated);
    assertFalse(generated.contains("Streaming operations are not wired"), generated);
    assertTrue(
        generated.contains(
            "private readonly"
                + " SmithyOperationBinding<Example.Example.Streaming.WatchInput,"
                + " Example.Example.Streaming.WatchOutput> WatchBinding;"));
    assertTrue(generated.contains("runtime.InvokeAsync(WatchBinding"), generated);
    assertFalse(generated.contains("SmithyEventStreamOperationInvoker"), generated);
  }

  @Test
  void outputOperationsReturnRuntimeTaskDirectly() throws Exception {
    String generated = renderClient();

    assertTrue(
        generated.contains("return runtime.InvokeAsync(WatchBinding, input, cancellationToken);"),
        generated);
    assertFalse(generated.contains("return await runtime.InvokeAsync"), generated);
  }

  @Test
  void grpcStreamingOperationsRejectSiblingMembers() {
    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () ->
                renderClient(
                    PROTOCOL_TRAITS,
                    STREAMING_SIBLING_MODEL,
                    "example.streaming#StreamingService",
                    "Example.Streaming"));

    assertTrue(
        ex.getMessage()
            .contains(
                "gRPC event-stream operation example.streaming#Chat input shape"
                    + " example.streaming#ChatInput must contain exactly one event-stream member"),
        ex.getMessage());
    assertTrue(ex.getMessage().contains("Members: events (event stream), room."), ex.getMessage());
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

  private static final String REST_STREAMING_MODEL =
      """
      $version: "2"

      namespace example.reststreaming

      use aws.protocols#restJson1
      use smithy.api#streaming

      @restJson1
      service StreamingService {
          version: "1"
          operations: [Watch]
      }

      @http(method: "GET", uri: "/watch")
      operation Watch {
          input := {}
          output := {
              @httpPayload
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

  @Test
  void restJson1EventStreamOperationsAreBoundThroughSharedRuntime() throws Exception {
    String generated =
        renderClient(
            REST_PROTOCOL_TRAITS,
            REST_STREAMING_MODEL,
            "example.reststreaming#StreamingService",
            "Example.RestStreaming");

    assertTrue(
        generated.contains(
            "private readonly"
                + " SmithyOperationBinding<Example.Example.Reststreaming.WatchInput,"
                + " Example.Example.Reststreaming.WatchOutput> WatchBinding;"),
        generated);
    assertTrue(
        generated.contains(
            "serviceProtocol.ForClientOperation(Example.Example.Reststreaming.WatchSchema.Schema)"),
        generated);
    assertTrue(generated.contains("runtime.InvokeAsync(WatchBinding"), generated);
    assertFalse(generated.contains("Event-stream operations are not supported"), generated);
  }

  @Test
  void operationBindingsCarryEffectiveAuthSchemes() throws Exception {
    String generated =
        renderClient(REST_PROTOCOL_TRAITS, AUTH_MODEL, "example.auth#Secured", "Example.Auth");

    // ReadThing inherits the service's effective schemes (alphabetical by shape id).
    assertTrue(
        generated.contains(
            "serviceProtocol.ForClientOperation(Example.Example.Auth.ReadThingSchema.Schema), new"
                + " string[] { \"smithy.api#httpApiKeyAuth\", \"smithy.api#httpBearerAuth\" });"),
        generated);
    // AdminThing's @auth trait overrides the service default.
    assertTrue(
        generated.contains(
            "serviceProtocol.ForClientOperation(Example.Example.Auth.AdminThingSchema.Schema), new"
                + " string[] { \"smithy.api#httpApiKeyAuth\" });"),
        generated);
  }

  private static final String PAGINATED_MODEL =
      """
      $version: "2"

      namespace example.pages

      use aws.protocols#restJson1

      @restJson1
      @paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
      service Catalog {
          version: "1"
          operations: [ListThings, GetThing]
      }

      @readonly
      @paginated(items: "items")
      @http(method: "GET", uri: "/things")
      operation ListThings {
          input := {
              @httpQuery("nextToken")
              nextToken: String

              @httpQuery("pageSize")
              pageSize: Integer
          }
          output := {
              nextToken: String

              @required
              items: Things
          }
      }

      @readonly
      @http(method: "GET", uri: "/things/{id}")
      operation GetThing {
          input := {
              @required
              @httpLabel
              id: String
          }
          output := {
              name: String
          }
      }

      list Things {
          member: Thing
      }

      structure Thing {
          @required
          name: String
      }
      """;

  @Test
  void paginatedOperationsGeneratePagesAndItemsPaginators() throws Exception {
    String generated =
        renderClient(
            REST_PROTOCOL_TRAITS, PAGINATED_MODEL, "example.pages#Catalog", "Example.Pages");

    // Pages paginator: repeats the call while the response carries a token.
    assertTrue(
        generated.contains(
            "System.Collections.Generic.IAsyncEnumerable<Example.Example.Pages.ListThingsOutput>"
                + " ListThingsPagesAsync(Example.Example.Pages.ListThingsInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(generated.contains("input = input with { NextToken = token };"), generated);
    assertTrue(generated.contains("while (token is not null)"), generated);

    // Items paginator: flattens the pages' list member.
    assertTrue(
        generated.contains(
            "System.Collections.Generic.IAsyncEnumerable<Example.Example.Pages.Thing>"
                + " ListThingsItemsAsync(Example.Example.Pages.ListThingsInput input,"
                + " System.Threading.CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(generated.contains("foreach (var item in items.Values)"), generated);

    // Unpaginated operations get no paginators.
    assertFalse(generated.contains("GetThingPagesAsync"), generated);
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

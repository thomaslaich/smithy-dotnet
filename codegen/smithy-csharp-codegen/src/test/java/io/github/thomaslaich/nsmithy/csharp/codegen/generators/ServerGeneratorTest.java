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

  private static final String MULTI_PROTOCOL_TRAITS =
      """
      $version: "2"

      namespace aws.protocols

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure restJson1 {}

      ---

      $version: "2"

      namespace alloy

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure simpleRestJson {}

      ---

      $version: "2"

      namespace smithy.protocols

      use smithy.api#protocolDefinition
      use smithy.api#trait

      @trait(selector: "service")
      @protocolDefinition
      structure rpcv2Cbor {}
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

  private static final String MULTI_PROTOCOL_MODEL =
      """
      $version: "2"

      namespace example.multi

      use alloy#simpleRestJson
      use aws.protocols#restJson1
      use smithy.protocols#rpcv2Cbor

      @rpcv2Cbor
      @simpleRestJson
      @restJson1
      service MultiService {
          version: "1"
          operations: [GetThing]
      }

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
      """;

  private static final String CATALOG_MODEL =
      """
      $version: "2"

      namespace example.catalog

      service CatalogService {
          version: "1"
          operations: [Ping, Notify]
      }

      operation Ping {
          output := {
              message: String
          }
      }

      operation Notify {
          input := {
              message: String
          }
      }
      """;

  private static final String PROMPTS_MODEL =
      """
      $version: "2"

      namespace example.prompts

      use smithy.ai#prompts

      @prompts({
          service_brief: {
              description: "Summarize two locations"
              template: "Compare {{first}} with {{second}}."
              arguments: ComparisonArguments
              preferWhen: "Use this for comparisons"
          }
      })
      service PromptService {
          version: "1"
          operations: [Compare]
      }

      @prompts({
          operation_brief: {
              description: "Use the comparison operation"
              template: "Call Compare for {{first}} and {{second}}."
              arguments: ComparisonArguments
          }
      })
      operation Compare {
          input: ComparisonArguments
          output := {}
      }

      structure ComparisonArguments {
          /// First location.
          @required
          first: String

          second: String
      }
      """;

  @Test
  void streamingGrpcServerUsesAsyncEnumerableHandlersAndStreamingWriter() throws Exception {
    String generated = renderServer();

    assertTrue(
        generated.contains(
            "Task<global::Example.Example.Streaming.WatchOutput>"
                + " WatchAsync(global::Example.Example.Streaming.WatchInput input,"
                + " CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "Task<global::Example.Example.Streaming.UploadOutput>"
                + " UploadAsync(global::Example.Example.Streaming.UploadInput input,"
                + " CancellationToken cancellationToken = default);"),
        generated);
    assertTrue(
        generated.contains(
            "Task<global::Example.Example.Streaming.ChatOutput>"
                + " ChatAsync(global::Example.Example.Streaming.ChatInput input,"
                + " CancellationToken cancellationToken = default);"),
        generated);
    assertFalse(generated.contains("IEventStreamServiceProtocol"));
    // Streaming operations are ordinary table entries; the protocol decides how bodies stream.
    assertTrue(
        generated.contains(
            "ServerOperation.Create(global::Example.Example.Streaming.Schemas.ChatSchema.Schema,"
                + " services => services.GetRequiredService<IChatHandler>().ChatAsync),"),
        generated);
    assertTrue(generated.contains("selected.Add(new(\"Grpc\", new GrpcProtocol()));"), generated);
    assertTrue(
        containsIgnoringWhitespace(
            generated,
            "public static RouteGroupBuilder MapStreamingService(this"
                + " IEndpointRouteBuilder endpoints, StreamingServiceProtocols protocols ="
                + " StreamingServiceProtocols.Grpc)"),
        generated);
    assertFalse(
        generated.contains("public static RouteGroupBuilder MapStreamingServiceGrpc"), generated);
  }

  @Test
  void restJson1EventStreamOperationsMapRestRoutes() throws Exception {
    String generated =
        renderServer(
            REST_PROTOCOL_TRAITS,
            REST_STREAMING_MODEL,
            "example.reststreaming#StreamingService",
            "Example.RestStreaming");

    assertTrue(generated.contains("public enum StreamingServiceProtocols"), generated);
    assertTrue(generated.contains("RestJson1 = 1,"), generated);
    assertTrue(generated.contains("All = RestJson1,"), generated);
    assertTrue(
        containsIgnoringWhitespace(
            generated,
            "public static RouteGroupBuilder MapStreamingService(this"
                + " IEndpointRouteBuilder endpoints, StreamingServiceProtocols protocols ="
                + " StreamingServiceProtocols.RestJson1)"),
        generated);
    assertFalse(
        generated.contains("public static RouteGroupBuilder MapStreamingServiceRestJson1"),
        generated);
    // Routes come from the protocol at runtime, not from generated code.
    assertFalse(generated.contains("MapMethods"), generated);
    assertTrue(
        generated.contains("services => services.GetRequiredService<IUploadHandler>().UploadAsync"),
        generated);
    assertTrue(
        generated.contains(
            "return endpoints.MapSmithyService(StreamingServiceDefinition.Catalog, selected);"),
        generated);
  }

  @Test
  void multiProtocolServersSelectProtocolsOverOneOperationTable() throws Exception {
    String generated =
        renderServer(
            MULTI_PROTOCOL_TRAITS,
            MULTI_PROTOCOL_MODEL,
            "example.multi#MultiService",
            "Example.Multi");

    assertTrue(generated.contains("public enum MultiServiceProtocols"), generated);
    assertTrue(generated.contains("RpcV2Cbor = 1,"), generated);
    assertTrue(generated.contains("SimpleRestJson = 2,"), generated);
    assertTrue(generated.contains("RestJson1 = 4,"), generated);
    assertTrue(generated.contains("All = RpcV2Cbor | SimpleRestJson | RestJson1,"), generated);
    assertTrue(
        containsIgnoringWhitespace(
            generated,
            "public static RouteGroupBuilder MapMultiService(this"
                + " IEndpointRouteBuilder endpoints, MultiServiceProtocols protocols ="
                + " MultiServiceProtocols.RpcV2Cbor)"),
        generated);
    assertTrue(generated.contains("if ((protocols & ~MultiServiceProtocols.All) != 0)"), generated);
    assertTrue(
        generated.contains("if ((protocols & MultiServiceProtocols.SimpleRestJson) != 0)"),
        generated);
    assertTrue(
        generated.contains("selected.Add(new(\"SimpleRestJson\", new SimpleRestJsonProtocol()));"),
        generated);
    assertTrue(
        generated.contains("selected.Add(new(\"RestJson1\", new RestJson1Protocol()));"),
        generated);
    // One table entry per operation, whatever the number of protocols.
    assertTrue(
        generated.indexOf("ServerOperation.Create(")
            == generated.lastIndexOf("ServerOperation.Create("),
        generated);
    assertFalse(
        generated.contains("public static RouteGroupBuilder MapMultiServiceRestJson1"), generated);
    assertFalse(
        generated.contains("public static RouteGroupBuilder MapMultiServiceRpcV2Cbor"), generated);
  }

  @Test
  void serverGeneratesProtocolNeutralOperationCatalog() throws Exception {
    String generated =
        renderServer("", CATALOG_MODEL, "example.catalog#CatalogService", "Example.Catalog");

    assertTrue(generated.contains("using NSmithy.Server;"), generated);
    assertTrue(
        generated.contains("public sealed class CatalogServiceDefinition : IServiceDefinition"),
        generated);
    assertTrue(
        generated.contains(
            "public static IServiceCollection AddCatalogService(this IServiceCollection"
                + " services)"),
        generated);
    assertTrue(generated.contains("services.AddCatalogService();"), generated);
    assertTrue(generated.contains("services.GetRequiredService<INotifyHandler>()"), generated);
    assertTrue(generated.contains("services.GetRequiredService<IPingHandler>()"), generated);
    assertFalse(generated.contains("CreateCatalogServiceOperationCatalog"), generated);
    assertTrue(generated.contains("CatalogServiceSchema.Schema,"), generated);
    assertTrue(generated.contains("private static class NotifyJsonSchemas"), generated);
    assertTrue(generated.contains("private static class PingJsonSchemas"), generated);
    assertTrue(
        generated.contains("public static OperationJsonSchemas Value { get; } = new("), generated);
    assertTrue(generated.contains("draft/2020-12/schema"), generated);
    // One table: each entry resolves its handler from the request's services.
    assertTrue(
        generated.contains("internal static ServiceOperationCatalog Catalog { get; } = new("),
        generated);
    assertTrue(
        generated.contains("public ServiceOperationCatalog Operations => Catalog;"), generated);
    assertTrue(
        generated.contains(
            "ServerOperation.Create(global::Example.Example.Catalog.Schemas.NotifySchema.Schema,"
                + " services => async (input, ct) => { await"
                + " services.GetRequiredService<INotifyHandler>().NotifyAsync(input,"
                + " ct).ConfigureAwait(false); return SmithyUnit.Value; },"
                + " NotifyJsonSchemas.Value),"),
        generated);
    assertTrue(
        generated.contains(
            "ServerOperation.Create(global::Example.Example.Catalog.Schemas.PingSchema.Schema,"
                + " services => (_, ct) =>"
                + " services.GetRequiredService<IPingHandler>().PingAsync(ct),"
                + " PingJsonSchemas.Value)"),
        generated);
  }

  @Test
  void serverOperationsCarryTheirEffectiveAuthSchemes() throws Exception {
    String generated =
        renderServer(
            "",
            """
            $version: "2"

            namespace example.auth

            @httpBearerAuth
            @httpApiKeyAuth(name: "X-Api-Key", in: "header")
            service AuthService {
                version: "1"
                operations: [Delete, Health, Lookup, Status]
            }

            operation Delete {}

            @auth([])
            operation Health {}

            @auth([httpApiKeyAuth])
            operation Lookup {}

            @optionalAuth
            operation Status {}
            """,
            "example.auth#AuthService",
            "Example.Auth");

    // Without @auth, every scheme on the service applies, in alphabetical order.
    assertTrue(
        generated.contains(
            "DeleteJsonSchemas.Value, authSchemes:"
                + " [ShapeId.Parse(\"smithy.api#httpApiKeyAuth\"),"
                + " ShapeId.Parse(\"smithy.api#httpBearerAuth\")]),"),
        generated);
    assertTrue(generated.contains("HealthJsonSchemas.Value),"), generated);
    assertTrue(
        generated.contains(
            "LookupJsonSchemas.Value, authSchemes:"
                + " [ShapeId.Parse(\"smithy.api#httpApiKeyAuth\")]),"),
        generated);
    assertTrue(
        generated.contains(
            "StatusJsonSchemas.Value, authSchemes:"
                + " [ShapeId.Parse(\"smithy.api#httpApiKeyAuth\"),"
                + " ShapeId.Parse(\"smithy.api#httpBearerAuth\"),"
                + " ShapeId.Parse(\"smithy.api#noAuth\")])"),
        generated);
  }

  @Test
  void serverGeneratesServiceAndOperationPromptDefinitions() throws Exception {
    String generated =
        renderServer("", PROMPTS_MODEL, "example.prompts#PromptService", "Example.Prompts");

    assertTrue(
        generated.contains("public IReadOnlyList<ServicePromptDefinition> Prompts"), generated);
    assertTrue(generated.contains("\"service_brief\""), generated);
    assertTrue(generated.contains("\"Summarize two locations\""), generated);
    assertTrue(generated.contains("\"Compare {{first}} with {{second}}.\""), generated);
    assertTrue(generated.contains("\"Use this for comparisons\""), generated);
    assertTrue(
        generated.contains(
            "new ServicePromptArgumentDefinition(\"first\", \"First location.\", true)"),
        generated);
    assertTrue(
        generated.contains("new ServicePromptArgumentDefinition(\"second\", null, false)"),
        generated);
    assertTrue(generated.contains("\"operation_brief\""), generated);
  }

  private static boolean containsIgnoringWhitespace(String actual, String expected) {
    return actual.replaceAll("\\s+", "").contains(expected.replaceAll("\\s+", ""));
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
    var assembler = Model.assembler();
    if (!protocolTraits.isBlank()) {
      String[] protocolTraitModels = protocolTraits.split("(?m)^---$");
      for (int i = 0; i < protocolTraitModels.length; i++) {
        assembler.addUnparsedModel("protocol-traits-" + i + ".smithy", protocolTraitModels[i]);
      }
    }
    Model model =
        assembler.addUnparsedModel("model.smithy", modelText).discoverModels().assemble().unwrap();
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
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider, model, settings))
            .build();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", writerNamespace);
    var service = model.expectShape(ShapeId.from(serviceId), ServiceShape.class);

    new ServerGenerator(context, writer, service).run();

    return writer.toString();
  }
}

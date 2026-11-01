package io.github.thomaslaich.nsmithy.fsharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class ServerGeneratorTest {

  private static final Path SERVER = Path.of("Server.g.fs");

  /** Stand-ins for the AWS and protocol traits packages, which define the real traits. */
  private static final String PROTOCOLS =
      """
      $version: "2"
      namespace aws.protocols

      @trait(selector: "service")
      @protocolDefinition
      structure restJson1 {}
      """;

  private static final String MODEL =
      """
      $version: "2"
      namespace example.cafe

      use aws.protocols#restJson1

      /// A cafe.
      @restJson1
      service Cafe {
          version: "2024-01-01"
          operations: [Order]
      }

      /// Orders a coffee.
      @http(method: "POST", uri: "/orders")
      operation Order {
          input := {
              type: String
          }
          output := {
              @required
              id: String
          }
      }
      """;

  @Test
  void generatesHandlersADefinitionAndExtensions() {
    String server = generate(MODEL, true).expectFileString(SERVER);

    assertTrue(server.contains("namespace Example.Cafe"), server);
    assertTrue(
        server.contains(
            "abstract OrderAsync: input: global.Example.Cafe.OrderInput * cancellationToken:"
                + " global.System.Threading.CancellationToken ->"
                + " global.System.Threading.Tasks.Task<global.Example.Cafe.OrderOutput>"),
        server);
    assertTrue(server.contains("type ICafeServiceHandler =\n    inherit IOrderHandler"), server);
    assertTrue(server.contains("type CafeServiceDefinition() ="), server);
    assertTrue(server.contains("global.NSmithy.Server.OperationJsonSchemas("), server);
    assertTrue(server.contains("static member AddCafeServiceHandler<'THandler"), server);
    assertTrue(server.contains("static member MapCafeService("), server);
  }

  @Test
  void selectsProtocolsAsFlags() {
    String server = generate(MODEL, true).expectFileString(SERVER);

    assertTrue(server.contains("[<global.System.Flags>]\ntype CafeServiceProtocols ="), server);
    assertTrue(server.contains("| RestJson1 = 1\n    | All = 1"), server);
    assertTrue(server.contains("global.NSmithy.Protocols.RestJson.RestJson1Protocol()"), server);
  }

  @Test
  void aServiceWithoutAServerProtocolHasNoEndpointMapping() {
    String server = generate(MODEL.replace("@restJson1\n", ""), true).expectFileString(SERVER);

    assertTrue(server.contains("type ICafeServiceHandler ="), server);
    assertFalse(server.contains("MapCafeService"), server);
    assertFalse(server.contains("AddSmithyServer"), server);
  }

  @Test
  void operationsCarryTheirEffectiveAuthSchemes() {
    String model =
        """
        $version: "2"
        namespace example.cafe

        use aws.protocols#restJson1

        @restJson1
        @httpBearerAuth
        @httpApiKeyAuth(name: "X-Api-Key", in: "header")
        service Cafe {
            version: "2024-01-01"
            operations: [Order, Menu, Status, Tab]
        }

        @http(method: "POST", uri: "/orders")
        operation Order {}

        @auth([])
        @http(method: "GET", uri: "/menu")
        operation Menu {}

        @optionalAuth
        @http(method: "GET", uri: "/status")
        operation Status {}

        @auth([httpApiKeyAuth])
        @http(method: "GET", uri: "/tab")
        operation Tab {}
        """;
    String server = generate(model, true).expectFileString(SERVER);
    String apiKey = "global.NSmithy.Core.ShapeId.Parse(\"smithy.api#httpApiKeyAuth\")";
    String bearer = "global.NSmithy.Core.ShapeId.Parse(\"smithy.api#httpBearerAuth\")";
    String noAuth = "global.NSmithy.Core.ShapeId.Parse(\"smithy.api#noAuth\")";

    // Without @auth, every scheme on the service applies, in alphabetical order.
    assertTrue(server.contains("authSchemes = [| " + apiKey + "; " + bearer + " |]"), server);
    assertTrue(server.contains("authSchemes = [| " + apiKey + " |]"), server);
    assertTrue(
        server.contains("authSchemes = [| " + apiKey + "; " + bearer + "; " + noAuth + " |]"),
        server);
    // @auth([]) leaves only noAuth, the runtime's default, so the argument is left out.
    assertEquals(3, server.split("authSchemes = ", -1).length - 1, server);
  }

  @Test
  void mapReturnsTheServicesRouteGroup() {
    String server = generate(MODEL, true).expectFileString(SERVER);

    assertTrue(
        server.contains(") : global.Microsoft.AspNetCore.Routing.RouteGroupBuilder ="), server);
  }

  @Test
  void generateServerFalseLeavesTheServerOut() {
    assertFalse(generate(MODEL, false).hasFile(SERVER));
  }

  private static MockManifest generate(String model, boolean generateServer) {
    Model assembled =
        Model.assembler()
            .addUnparsedModel("protocols.smithy", PROTOCOLS)
            .addUnparsedModel("model.smithy", model)
            .assemble()
            .unwrap();
    ObjectNode settings =
        Node.objectNodeBuilder()
            .withMember("service", "example.cafe#Cafe")
            .withMember("generateServer", generateServer)
            .build();
    MockManifest manifest = new MockManifest();
    FSharpCodegenPlugin.generate(assembled, FSharpSettings.fromNode(settings), manifest, List.of());
    return manifest;
  }
}

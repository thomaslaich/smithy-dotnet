package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.build.MockManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;

class ProtocolIntegrationTest {

  private static final String MODEL =
      """
      $version: "2"
      namespace example.weather

      use acme.protocols#msgpack

      @msgpack
      service Weather {
          version: "2024-01-01"
          operations: [GetCity]
      }

      operation GetCity {
          input := { cityId: String }
          output := { name: String }
      }
      """;

  private static final String MSGPACK_TRAIT =
      """
      $version: "2"
      namespace acme.protocols

      @trait(selector: "service")
      @protocolDefinition
      structure msgpack {}
      """;

  @Test
  void anIntegrationProtocolGetsAClientAndAServer() {
    MockManifest manifest = generate();

    String client = manifest.expectFileString(Path.of("Example/Weather/Weather.Client.g.cs"));
    assertTrue(client.contains("MsgPackProtocol()"), client);

    String server = manifest.expectFileString(Path.of("Example/Weather/Weather.Server.g.cs"));
    assertTrue(server.contains("MsgPack = 1,"), server);
    assertTrue(server.contains("selected.Add(new(\"MsgPack\", new "), server);
    assertTrue(server.contains("MsgPackProtocol()));"), server);
  }

  private static MockManifest generate() {
    Model model =
        Model.assembler()
            .addUnparsedModel("msgpack.smithy", MSGPACK_TRAIT)
            .addUnparsedModel("weather.smithy", MODEL)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.weather#Weather"))
                .withMember("baseNamespace", Node.from(""))
                .build());
    MockManifest manifest = new MockManifest();

    CSharpCodegenPlugin.generate(model, settings, manifest, List.of(new MsgPackIntegration()));
    return manifest;
  }

  private static final class MsgPackIntegration implements CSharpIntegration {
    @Override
    public String name() {
      return "msgpack";
    }

    @Override
    public List<CSharpProtocol> protocols() {
      return List.of(
          new CSharpProtocol(
              ShapeId.from("acme.protocols#msgpack"),
              "MsgPack",
              CSharpProtocol.runtimeType("Acme.Protocols.MsgPackProtocol"),
              false,
              true));
    }
  }
}

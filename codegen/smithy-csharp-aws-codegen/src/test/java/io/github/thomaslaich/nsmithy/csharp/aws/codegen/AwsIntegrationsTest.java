package io.github.thomaslaich.nsmithy.csharp.aws.codegen;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpCodegenPlugin;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ServiceLoader;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class AwsIntegrationsTest {

  @TempDir Path tempDir;

  @Test
  void theIntegrationsAreDiscoveredThroughTheServiceLoader() {
    var types =
        ServiceLoader.load(CSharpIntegration.class).stream()
            .map(ServiceLoader.Provider::type)
            .toList();

    assertTrue(types.contains(AwsProtocolsIntegration.class), types.toString());
    assertTrue(types.contains(AwsEndpointsIntegration.class), types.toString());
    assertTrue(types.contains(GlacierIntegration.class), types.toString());
  }

  @Test
  void anAwsJsonServiceGetsAClientOverTheAwsJsonProtocol() throws Exception {
    String client =
        generate(
            """
            $version: "2"
            namespace example.queue

            use aws.protocols#awsJson1_0

            @awsJson1_0
            service Queue {
                version: "2024-01-01"
                operations: [Send]
            }

            operation Send {
                input := { body: String }
            }
            """,
            "example.queue#Queue",
            "Example/Queue/Queue.Client.g.cs");

    assertTrue(client.contains("AwsJson10Protocol()"), client);
  }

  @Test
  void theGlacierClientConfigAddsTheGlacierInterceptor() throws Exception {
    String client =
        generate(
            glacierModel("com.amazonaws.glacier"),
            "com.amazonaws.glacier#Glacier",
            "Com/Amazonaws/Glacier/Glacier.Client.g.cs");

    assertTrue(client.contains("Interceptors.Add(new GlacierInterceptor());"), client);
  }

  @Test
  void anotherServiceNamedGlacierDoesNot() throws Exception {
    String client =
        generate(
            glacierModel("example.glacier"),
            "example.glacier#Glacier",
            "Example/Glacier/Glacier.Client.g.cs");

    assertFalse(client.contains("GlacierInterceptor"), client);
  }

  @Test
  void aServiceWhoseRulesReadTheRegionDefaultsItFromTheEnvironment() throws Exception {
    String client =
        generate(
            regionModel("builtIn: \"AWS::Region\"", ""),
            "example.regional#Regional",
            "Example/Regional/Regional.Client.g.cs");

    assertTrue(client.contains("Region = AwsRegion.Resolve();"), client);
  }

  @Test
  void aServiceWhoseRulesDoNotReadTheRegionGetsNoDefault() throws Exception {
    String client =
        generate(
            regionModel(
                "documentation: \"A client setting.\"",
                "@clientContextParams(Region: { type: \"string\", documentation: \"A client"
                    + " setting.\" })"),
            "example.regional#Regional",
            "Example/Regional/Regional.Client.g.cs");

    assertFalse(client.contains("AwsRegion"), client);
  }

  private static String regionModel(String regionParameter, String serviceTrait) {
    return """
    $version: "2"
    metadata suppressions = [{ id: "UnstableTrait", namespace: "example.regional" }]
    namespace example.regional

    use aws.protocols#awsJson1_0
    use smithy.rules#clientContextParams
    use smithy.rules#endpointRuleSet

    %s
    @awsJson1_0
    @endpointRuleSet({
        version: "1.0"
        parameters: { Region: { type: "string", %s } }
        rules: [
            {
                conditions: [{ fn: "isSet", argv: [{ ref: "Region" }] }]
                type: "endpoint"
                endpoint: { url: "https://regional.{Region}.example.com" }
            }
            { conditions: [], type: "error", error: "Region is required" }
        ]
    })
    service Regional {
        version: "2024-01-01"
        operations: [Ping]
    }

    operation Ping {}
    """
        .formatted(serviceTrait, regionParameter);
  }

  private static String glacierModel(String namespace) {
    return """
    $version: "2"
    namespace %s

    use aws.protocols#restJson1

    @restJson1
    service Glacier {
        version: "2012-06-01"
        operations: [ListVaults]
    }

    @http(method: "GET", uri: "/{accountId}/vaults")
    operation ListVaults {
        input := { @required @httpLabel accountId: String }
    }
    """
        .formatted(namespace);
  }

  private String generate(String model, String service, String file) throws Exception {
    Model assembled =
        Model.assembler()
            .discoverModels(AwsIntegrationsTest.class.getClassLoader())
            .addUnparsedModel("model.smithy", model)
            .assemble()
            .unwrap();
    new CSharpCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(assembled)
                .fileManifest(FileManifest.create(tempDir))
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from(service))
                        .withMember("baseNamespace", Node.from(""))
                        .build())
                .build());
    return Files.readString(tempDir.resolve(file));
  }
}

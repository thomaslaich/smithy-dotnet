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

  @Test
  void smokeTestVendorParamsSetTheBuiltInsTheRulesRead() throws Exception {
    String tests = smokeTests();

    assertTrue(tests.contains("configured.Region = \"eu-west-1\";"), tests);
    assertTrue(tests.contains("configured.UseFips = true;"), tests);
    assertTrue(tests.contains("configured.ForcePathStyle = true;"), tests);
    assertTrue(tests.contains("configured.Endpoint = new Uri(\"https://localhost:4566\");"), tests);
    // The rules read neither built-in.
    assertFalse(tests.contains("UseDualStack"), tests);
    assertFalse(tests.contains("DisableMultiRegionAccessPoints"), tests);
  }

  @Test
  void smokeTestVendorParamsDefaultToTheirShapesDefaults() throws Exception {
    String tests = smokeTests();

    assertTrue(tests.contains("configured.Region = \"us-west-2\";"), tests);
    assertTrue(tests.contains("configured.UseFips = false;"), tests);
  }

  private String smokeTests() throws Exception {
    Model assembled =
        Model.assembler()
            .discoverModels(AwsIntegrationsTest.class.getClassLoader())
            .addUnparsedModel(
                "model.smithy",
                """
                $version: "2"
                metadata suppressions = [{ id: "UnstableTrait", namespace: "example.buckets" }]
                namespace example.buckets

                use aws.protocols#restXml
                use aws.test#AwsVendorParams
                use aws.test#S3VendorParams
                use smithy.rules#endpointRuleSet
                use smithy.test#smokeTests

                @restXml
                @endpointRuleSet({
                    version: "1.0"
                    parameters: {
                        Region: { type: "string", builtIn: "AWS::Region" }
                        UseFIPS: { type: "boolean", builtIn: "AWS::UseFIPS", required: true, default: false }
                        ForcePathStyle: { type: "boolean", builtIn: "AWS::S3::ForcePathStyle", required: true, default: false }
                    }
                    rules: [
                        {
                            conditions: [{ fn: "isSet", argv: [{ ref: "Region" }] }]
                            type: "endpoint"
                            endpoint: { url: "https://buckets.{Region}.example.com" }
                        }
                        { conditions: [], type: "error", error: "Region is required" }
                    ]
                })
                service Buckets {
                    version: "2024-01-01"
                    operations: [ListBuckets]
                }

                @readonly
                @http(method: "GET", uri: "/")
                @smokeTests([
                    {
                        id: "ListBucketsInIreland"
                        vendorParamsShape: S3VendorParams
                        vendorParams: {
                            region: "eu-west-1"
                            useFips: true
                            forcePathStyle: true
                            useMultiRegionAccessPoints: false
                            uri: "https://localhost:4566"
                        }
                        expect: { success: {} }
                    }
                    {
                        id: "ListBucketsByDefault"
                        vendorParamsShape: AwsVendorParams
                        expect: { success: {} }
                    }
                ])
                operation ListBuckets {}
                """)
            .assemble()
            .unwrap();
    new CSharpCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(assembled)
                .fileManifest(FileManifest.create(tempDir))
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.buckets#Buckets"))
                        .withMember("baseNamespace", Node.from(""))
                        .withMember("generateServer", Node.from(false))
                        .withMember("generateSmokeTests", Node.from(true))
                        .build())
                .build());
    return Files.readString(tempDir.resolve("Example/Buckets/Buckets.SmokeTests.Client.g.cs"));
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
                        // A client of an AWS service; its model does not declare
                        // ValidationException.
                        .withMember("generateServer", Node.from(false))
                        .build())
                .build());
    return Files.readString(tempDir.resolve(file));
  }
}

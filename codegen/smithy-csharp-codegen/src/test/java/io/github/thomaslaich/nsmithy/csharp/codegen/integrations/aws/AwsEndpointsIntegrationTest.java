package io.github.thomaslaich.nsmithy.csharp.codegen.integrations.aws;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointBuiltIn;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointDefaultAuthScheme;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointFunction;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import java.util.List;
import java.util.Map;
import java.util.ServiceLoader;
import java.util.Set;
import java.util.stream.Collectors;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DynamicTrait;
import software.amazon.smithy.model.traits.HttpTrait;

class AwsEndpointsIntegrationTest {

  @Test
  void isDiscoveredThroughTheServiceLoader() {
    assertTrue(
        ServiceLoader.load(CSharpIntegration.class).stream()
            .anyMatch(provider -> provider.type() == AwsEndpointsIntegration.class));
  }

  @Test
  void suppliesTheAwsFunctionsBuiltInsAndAuthSchemeIds() {
    var integration = new AwsEndpointsIntegration();

    assertEquals(
        Set.of("aws.partition", "aws.parseArn", "aws.isVirtualHostableS3Bucket"),
        integration.endpointFunctions().stream()
            .map(EndpointFunction::id)
            .collect(Collectors.toSet()));
    assertTrue(
        integration.endpointBuiltIns().stream()
            .anyMatch(
                builtIn ->
                    builtIn.equals(
                        new EndpointBuiltIn(
                            "AWS::Region",
                            "Region",
                            "The AWS region requests are sent to, such as us-east-1."))));
    assertEquals("aws.auth#sigv4", integration.endpointAuthSchemeIds().get("sigv4"));
  }

  @Test
  void endpointsWithoutAuthSchemesSignWithTheSigV4NameAndTheConfiguredRegion() {
    var service =
        ServiceShape.builder()
            .id("example#Weather")
            .version("1")
            .addTrait(
                new DynamicTrait(
                    ShapeId.from("aws.auth#sigv4"),
                    Node.objectNode().withMember("name", "weather")))
            .build();

    assertEquals(
        List.of(
            new EndpointDefaultAuthScheme(
                "aws.auth#sigv4",
                Map.of("signingName", "weather"),
                Map.of("signingRegion", "AWS::Region"))),
        new AwsEndpointsIntegration().endpointDefaultAuthSchemes(service));
  }

  @Test
  void s3OperationsLoseTheBucketPathSegment() {
    Model model =
        Model.assembler()
            .discoverModels(getClass().getClassLoader())
            .addUnparsedModel(
                "s3.smithy",
                """
                $version: "2"
                metadata suppressions = [{ id: "UnstableTrait", namespace: "com.amazonaws.s3" }]
                namespace com.amazonaws.s3

                use smithy.rules#endpointRuleSet

                @endpointRuleSet({
                    version: "1.0"
                    parameters: {}
                    rules: [{ conditions: [], type: "endpoint", endpoint: { url: "https://s3.example.com" } }]
                })
                service AmazonS3 {
                    version: "2006-03-01"
                    operations: [GetObject, HeadBucket, ListObjectsV2, ListBuckets]
                }

                @http(method: "GET", uri: "/{Bucket}/{Key+}?x-id=GetObject")
                operation GetObject {
                    input := {
                        @required @httpLabel Bucket: String
                        @required @httpLabel Key: String
                    }
                }

                @http(method: "HEAD", uri: "/{Bucket}")
                operation HeadBucket {
                    input := { @required @httpLabel Bucket: String }
                }

                @http(method: "GET", uri: "/{Bucket}?list-type=2")
                operation ListObjectsV2 {
                    input := { @required @httpLabel Bucket: String }
                }

                @http(method: "GET", uri: "/?x-id=ListBuckets")
                operation ListBuckets {}
                """)
            .assemble()
            .unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("com.amazonaws.s3#AmazonS3"))
                .build());

    Model transformed = new AwsEndpointsIntegration().preprocessModel(model, settings);

    assertEquals("/{Key+}?x-id=GetObject", uri(transformed, "GetObject"));
    assertEquals("/", uri(transformed, "HeadBucket"));
    assertEquals("/?list-type=2", uri(transformed, "ListObjectsV2"));
    assertEquals("/?x-id=ListBuckets", uri(transformed, "ListBuckets"));
  }

  private static String uri(Model model, String operation) {
    return model
        .expectShape(ShapeId.from("com.amazonaws.s3#" + operation))
        .expectTrait(HttpTrait.class)
        .getUri()
        .toString();
  }
}

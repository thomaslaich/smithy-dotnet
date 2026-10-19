package io.github.thomaslaich.nsmithy.csharp.codegen.integrations.aws;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

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
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DynamicTrait;

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
}

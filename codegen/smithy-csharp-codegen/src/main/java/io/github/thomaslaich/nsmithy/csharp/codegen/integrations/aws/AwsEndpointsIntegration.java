/*
 * The AWS endpoint rules functions, built-ins, and auth scheme names.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.integrations.aws;

import static io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointValueType.field;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointBuiltIn;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointDefaultAuthScheme;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointFunction;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointValueType;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.pattern.UriPattern;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.HttpTrait;
import software.amazon.smithy.model.transform.ModelTransformer;
import software.amazon.smithy.rulesengine.traits.EndpointBddTrait;
import software.amazon.smithy.rulesengine.traits.EndpointRuleSetTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Supplies what AWS endpoint rule sets need beyond the standard library: the {@code aws.*}
 * functions of {@code NSmithy.Aws.AwsEndpointFunctions}, the {@code AWS::*} built-ins as client
 * configuration properties, the auth trait ids of the {@code sigv4} and {@code sigv4a} endpoint
 * auth schemes, and the SigV4 signing scope of endpoints whose rules set none.
 */
@SmithyInternalApi
public final class AwsEndpointsIntegration implements CSharpIntegration {

  private static final ShapeId SIGV4 = ShapeId.from("aws.auth#sigv4");
  private static final ShapeId S3 = ShapeId.from("com.amazonaws.s3#AmazonS3");

  /**
   * S3's endpoint rules place the bucket in the host or, for path-style addressing, in the
   * endpoint's path. The modeled {@code /{Bucket}} path segment would repeat it, so it is removed
   * from the operations' URIs. {@code Bucket} keeps its {@code @httpLabel} binding, so it stays
   * required and out of the body.
   */
  @Override
  public Model preprocessModel(Model model, CSharpSettings settings) {
    if (!settings.service().filter(S3::equals).isPresent()
        || model
            .getShape(S3)
            .filter(
                service ->
                    service.hasTrait(EndpointBddTrait.class)
                        || service.hasTrait(EndpointRuleSetTrait.class))
            .isEmpty()) {
      return model;
    }
    List<Shape> updated = new ArrayList<>();
    for (OperationShape operation : TopDownIndex.of(model).getContainedOperations(S3)) {
      operation
          .getTrait(HttpTrait.class)
          .flatMap(
              http ->
                  withoutBucket(http.getUri().toString())
                      .map(uri -> http.toBuilder().uri(UriPattern.parse(uri)).build()))
          .ifPresent(http -> updated.add(operation.toBuilder().addTrait(http).build()));
    }
    return ModelTransformer.create().replaceShapes(model, updated);
  }

  private static Optional<String> withoutBucket(String uri) {
    String bucket = "/{Bucket}";
    if (!uri.startsWith(bucket)) {
      return Optional.empty();
    }
    String rest = uri.substring(bucket.length());
    if (rest.isEmpty() || rest.startsWith("?")) {
      return Optional.of("/" + rest);
    }
    return rest.startsWith("/") ? Optional.of(rest) : Optional.empty();
  }

  private static final EndpointValueType PARTITION =
      EndpointValueType.record(
          RuntimeTypes.AWS_PARTITION,
          Map.of(
              "name", field("Name", EndpointValueType.STRING),
              "dnsSuffix", field("DnsSuffix", EndpointValueType.STRING),
              "dualStackDnsSuffix", field("DualStackDnsSuffix", EndpointValueType.STRING),
              "supportsFIPS", field("SupportsFips", EndpointValueType.BOOLEAN),
              "supportsDualStack", field("SupportsDualStack", EndpointValueType.BOOLEAN),
              "implicitGlobalRegion", field("ImplicitGlobalRegion", EndpointValueType.STRING)));

  private static final EndpointValueType ARN =
      EndpointValueType.record(
          RuntimeTypes.AWS_ARN,
          Map.of(
              "partition", field("Partition", EndpointValueType.STRING),
              "service", field("Service", EndpointValueType.STRING),
              "region", field("Region", EndpointValueType.STRING),
              "accountId", field("AccountId", EndpointValueType.STRING),
              "resourceId", field("ResourceId", EndpointValueType.STRING_LIST)));

  @Override
  public String name() {
    return "aws-endpoints";
  }

  @Override
  public List<EndpointFunction> endpointFunctions() {
    return List.of(
        new EndpointFunction(
            "aws.partition", RuntimeTypes.AWS_ENDPOINT_FUNCTIONS, "Partition", PARTITION, false),
        new EndpointFunction(
            "aws.parseArn", RuntimeTypes.AWS_ENDPOINT_FUNCTIONS, "ParseArn", ARN, true),
        new EndpointFunction(
            "aws.isVirtualHostableS3Bucket",
            RuntimeTypes.AWS_ENDPOINT_FUNCTIONS,
            "IsVirtualHostableS3Bucket",
            EndpointValueType.BOOLEAN,
            false));
  }

  @Override
  public List<EndpointBuiltIn> endpointBuiltIns() {
    return List.of(
        new EndpointBuiltIn(
            "AWS::Region", "Region", "The AWS region requests are sent to, such as us-east-1."),
        new EndpointBuiltIn("AWS::UseFIPS", "UseFips", "Whether to use FIPS endpoints."),
        new EndpointBuiltIn(
            "AWS::UseDualStack",
            "UseDualStack",
            "Whether to use dual-stack (IPv4 and IPv6) endpoints."),
        new EndpointBuiltIn(
            "AWS::Auth::AccountId",
            "AccountId",
            "The AWS account id, for account-based endpoints."),
        new EndpointBuiltIn(
            "AWS::Auth::AccountIdEndpointMode",
            "AccountIdEndpointMode",
            "Whether account-based endpoints are used: preferred, required, or disabled."),
        new EndpointBuiltIn(
            "AWS::Auth::CredentialScope",
            "CredentialScope",
            "The credential scope, for scoped endpoints."),
        new EndpointBuiltIn(
            "AWS::S3::Accelerate", "Accelerate", "Whether to use S3 Transfer Acceleration."),
        new EndpointBuiltIn(
            "AWS::S3::DisableMultiRegionAccessPoints",
            "DisableMultiRegionAccessPoints",
            "Whether to reject S3 Multi-Region Access Point ARNs."),
        new EndpointBuiltIn(
            "AWS::S3::ForcePathStyle",
            "ForcePathStyle",
            "Whether to address S3 buckets in the path instead of the host name."),
        new EndpointBuiltIn(
            "AWS::S3::UseArnRegion",
            "UseArnRegion",
            "Whether the region in an ARN takes precedence over the configured region."),
        new EndpointBuiltIn(
            "AWS::S3::UseGlobalEndpoint",
            "UseGlobalEndpoint",
            "Whether us-east-1 requests use the global S3 endpoint."),
        new EndpointBuiltIn(
            "AWS::S3Control::UseArnRegion",
            "UseArnRegion",
            "Whether the region in an ARN takes precedence over the configured region."),
        new EndpointBuiltIn(
            "AWS::STS::UseGlobalEndpoint",
            "UseGlobalEndpoint",
            "Whether us-east-1 requests use the global STS endpoint."));
  }

  @Override
  public Map<String, String> endpointAuthSchemeIds() {
    return Map.of("sigv4", "aws.auth#sigv4", "sigv4a", "aws.auth#sigv4a");
  }

  /**
   * Endpoints whose rules set no auth schemes sign SigV4 with the {@code @sigv4} trait's name and
   * the configured region.
   */
  @Override
  public List<EndpointDefaultAuthScheme> endpointDefaultAuthSchemes(ServiceShape service) {
    return service
        .findTrait(SIGV4)
        .flatMap(trait -> trait.toNode().asObjectNode())
        .flatMap(node -> node.getStringMember("name"))
        .map(
            name ->
                List.of(
                    new EndpointDefaultAuthScheme(
                        SIGV4.toString(),
                        Map.of("signingName", name.getValue()),
                        Map.of("signingRegion", "AWS::Region"))))
        .orElse(List.of());
  }
}

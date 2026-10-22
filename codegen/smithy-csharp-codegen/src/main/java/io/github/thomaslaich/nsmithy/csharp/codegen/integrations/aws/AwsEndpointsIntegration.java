/*
 * The AWS endpoint rules functions, built-ins, and auth scheme names.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.integrations.aws;

import static io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointValueType.field;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointBuiltIn;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointDefaultAuthScheme;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointFunction;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointValueType;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.HttpTrait;
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

  @Override
  public Optional<String> writeEndpointRequestPathAdapter(
      GenerationContext context, ServiceShape service, CSharpWriter writer) {
    if (!service.getId().equals(S3)) {
      return Optional.empty();
    }
    List<String> bucketOperations =
        TopDownIndex.of(context.model()).getContainedOperations(service).stream()
            .filter(
                operation ->
                    operation
                        .getTrait(HttpTrait.class)
                        .map(http -> http.getUri().toString())
                        .filter(
                            uri ->
                                uri.equals("/{Bucket}")
                                    || uri.startsWith("/{Bucket}/")
                                    || uri.startsWith("/{Bucket}?"))
                        .isPresent())
            .map(operation -> operation.getId().getName())
            .sorted()
            .toList();
    if (bucketOperations.isEmpty()) {
      return Optional.empty();
    }
    writer.write("// S3 endpoints already place Bucket in the host or base path.");
    writer.write(
        "private static string PrepareRequestPath($T operationId, string requestPath) =>"
            + " operationId.Name switch",
        RuntimeTypes.SHAPE_ID);
    writer.write("{");
    writer.indent();
    for (int i = 0; i < bucketOperations.size(); i++) {
      writer.write("$L$L", i == 0 ? "" : "or ", CSharpNaming.formatString(bucketOperations.get(i)));
    }
    writer.write("    => $T.RemoveBucket(requestPath),", RuntimeTypes.AWS_S3_ENDPOINT_PATH);
    writer.write("_ => requestPath,");
    writer.dedent();
    writer.write("};");
    writer.write("");
    return Optional.of("PrepareRequestPath");
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

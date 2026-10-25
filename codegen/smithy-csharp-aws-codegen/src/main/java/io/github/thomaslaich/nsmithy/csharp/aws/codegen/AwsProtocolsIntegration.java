/*
 * The AWS request/response protocols, registered as any integration's protocols are.
 */
package io.github.thomaslaich.nsmithy.csharp.aws.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
import java.util.List;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

/** The AWS protocols NSmithy generates clients for. None of them serves requests. */
@SmithyInternalApi
public final class AwsProtocolsIntegration implements CSharpIntegration {

  @Override
  public String name() {
    return "aws-protocols";
  }

  @Override
  public List<CSharpProtocol> protocols() {
    return List.of(
        client("aws.protocols#restXml", "RestXml", AwsRuntimeTypes.REST_XML_PROTOCOL),
        client("aws.protocols#awsQuery", "AwsQuery", AwsRuntimeTypes.AWS_QUERY_PROTOCOL),
        client("aws.protocols#ec2Query", "Ec2Query", AwsRuntimeTypes.EC2_QUERY_PROTOCOL),
        client("aws.protocols#awsJson1_1", "AwsJson1_1", AwsRuntimeTypes.AWS_JSON11_PROTOCOL),
        client("aws.protocols#awsJson1_0", "AwsJson1_0", AwsRuntimeTypes.AWS_JSON10_PROTOCOL));
  }

  private static CSharpProtocol client(String trait, String name, Symbol runtimeType) {
    return new CSharpProtocol(ShapeId.from(trait), name, runtimeType, false, false);
  }
}

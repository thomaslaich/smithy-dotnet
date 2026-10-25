/*
 * The C# runtime types generated code for AWS services references.
 */
package io.github.thomaslaich.nsmithy.csharp.aws.codegen;

import static io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol.runtimeType;

import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class AwsRuntimeTypes {
  static final Symbol AWS_JSON10_PROTOCOL =
      runtimeType("NSmithy.Protocols.AwsJson.AwsJson10Protocol");
  static final Symbol AWS_JSON11_PROTOCOL =
      runtimeType("NSmithy.Protocols.AwsJson.AwsJson11Protocol");
  static final Symbol AWS_QUERY_PROTOCOL =
      runtimeType("NSmithy.Protocols.AwsQuery.AwsQueryProtocol");
  static final Symbol EC2_QUERY_PROTOCOL =
      runtimeType("NSmithy.Protocols.AwsQuery.Ec2QueryProtocol");
  static final Symbol REST_XML_PROTOCOL = runtimeType("NSmithy.Protocols.RestXml.RestXmlProtocol");

  static final Symbol AWS_ARN = runtimeType("NSmithy.Aws.AwsArn");
  static final Symbol AWS_ENDPOINT_FUNCTIONS = runtimeType("NSmithy.Aws.AwsEndpointFunctions");
  static final Symbol AWS_PARTITION = runtimeType("NSmithy.Aws.AwsPartition");
  static final Symbol GLACIER_INTERCEPTOR = runtimeType("NSmithy.Aws.GlacierInterceptor");

  private AwsRuntimeTypes() {}
}

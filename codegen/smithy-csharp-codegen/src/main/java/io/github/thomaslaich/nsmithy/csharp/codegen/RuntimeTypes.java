/*
 * Centralised C# runtime namespaces / type names that the generated code
 * references. Prevents typos from leaking into generators.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class RuntimeTypes {
  public static final String NSMITHY_CORE = "NSmithy.Core";
  public static final String NSMITHY_CORE_ANNOTATIONS = "NSmithy.Core.Annotations";
  public static final String NSMITHY_CORE_SERDE = "NSmithy.Core.Serde";
  public static final String NSMITHY_CORE_VALIDATION = "NSmithy.Core.Validation";
  public static final String NSMITHY_CLIENT = "NSmithy.Client";
  public static final String NSMITHY_PROTOCOLS_AWSJSON = "NSmithy.Protocols.AwsJson";
  public static final String NSMITHY_PROTOCOLS_AWSQUERY = "NSmithy.Protocols.AwsQuery";
  public static final String NSMITHY_PROTOCOLS_RESTJSON = "NSmithy.Protocols.RestJson";
  public static final String NSMITHY_PROTOCOLS_RESTXML = "NSmithy.Protocols.RestXml";
  public static final String NSMITHY_PROTOCOLS_RPCV2CBOR = "NSmithy.Protocols.RpcV2Cbor";
  public static final String NSMITHY_PROTOCOLS_GRPC = "NSmithy.Protocols.Grpc";
  public static final String NSMITHY_HTTP = "NSmithy.Http";
  public static final String NSMITHY_CODECS_JSON = "NSmithy.Codecs.Json";
  public static final String NSMITHY_CODECS_XML = "NSmithy.Codecs.Xml";
  public static final String NSMITHY_CODECS_CBOR = "NSmithy.Codecs.Cbor";
  public static final String NSMITHY_CODECS_PROTO = "NSmithy.Codecs.Proto";
  public static final String NSMITHY_SERVER = "NSmithy.Server";
  public static final String NSMITHY_SERVER_ASPNETCORE = "NSmithy.Server.AspNetCore";

  public static final String MS_EXT_DI = "Microsoft.Extensions.DependencyInjection";
  public static final String MS_EXT_DI_EXTENSIONS =
      "Microsoft.Extensions.DependencyInjection.Extensions";
  public static final String MS_ASPNETCORE_BUILDER = "Microsoft.AspNetCore.Builder";
  public static final String MS_ASPNETCORE_HTTP = "Microsoft.AspNetCore.Http";
  public static final String MS_ASPNETCORE_ROUTING = "Microsoft.AspNetCore.Routing";

  public static final String GRPC_CORE = "Grpc.Core";
  public static final String GOOGLE_PROTOBUF_WELLKNOWN = "Google.Protobuf.WellKnownTypes";

  public static final String SYSTEM = "System";
  public static final String SYSTEM_COLLECTIONS_GENERIC = "System.Collections.Generic";
  public static final String SYSTEM_NET = "System.Net";
  public static final String SYSTEM_NET_HTTP = "System.Net.Http";
  public static final String SYSTEM_TEXT = "System.Text";
  public static final String SYSTEM_THREADING = "System.Threading";
  public static final String SYSTEM_THREADING_TASKS = "System.Threading.Tasks";

  private RuntimeTypes() {}
}

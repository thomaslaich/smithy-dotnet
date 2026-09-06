/* External C# types referenced by generated code. */
package io.github.thomaslaich.nsmithy.csharp.codegen;

import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class RuntimeTypes {
  // System
  public static final Symbol ACTION = type("System.Action");
  public static final Symbol ARGUMENT_NULL_EXCEPTION = type("System.ArgumentNullException");
  public static final Symbol ARGUMENT_OUT_OF_RANGE_EXCEPTION =
      type("System.ArgumentOutOfRangeException");
  public static final Symbol ARRAY = type("System.Array");
  public static final Symbol BIG_INTEGER = type("System.Numerics.BigInteger");
  public static final Symbol CANCELLATION_TOKEN = type("System.Threading.CancellationToken");
  public static final Symbol CONVERT = type("System.Convert");
  public static final Symbol CULTURE_INFO = type("System.Globalization.CultureInfo");
  public static final Symbol DATE_TIME_OFFSET = type("System.DateTimeOffset");
  public static final Symbol DICTIONARY = type("System.Collections.Generic.Dictionary");
  public static final Symbol ENCODING = type("System.Text.Encoding");
  public static final Symbol ENUMERABLE = type("System.Linq.Enumerable");
  public static final Symbol ENUMERATOR_CANCELLATION_ATTRIBUTE =
      type("System.Runtime.CompilerServices.EnumeratorCancellationAttribute");
  public static final Symbol EXCEPTION = type("System.Exception");
  public static final Symbol FLAGS_ATTRIBUTE = type("System.FlagsAttribute");
  public static final Symbol FUNC = type("System.Func");
  public static final Symbol GUID = type("System.Guid");
  public static final Symbol HASH_SET = type("System.Collections.Generic.HashSet");
  public static final Symbol HTTP_CLIENT = type("System.Net.Http.HttpClient");
  public static final Symbol HTTP_VERSION = type("System.Net.HttpVersion");
  public static final Symbol INVALID_OPERATION_EXCEPTION = type("System.InvalidOperationException");
  public static final Symbol I_ASYNC_ENUMERABLE =
      type("System.Collections.Generic.IAsyncEnumerable");
  public static final Symbol I_DISPOSABLE = type("System.IDisposable");
  public static final Symbol I_ENUMERABLE = type("System.Collections.Generic.IEnumerable");
  public static final Symbol I_READ_ONLY_DICTIONARY =
      type("System.Collections.Generic.IReadOnlyDictionary");
  public static final Symbol I_READ_ONLY_LIST = type("System.Collections.Generic.IReadOnlyList");
  public static final Symbol LIST = type("System.Collections.Generic.List");
  public static final Symbol MEMORY_STREAM = type("System.IO.MemoryStream");
  public static final Symbol NOT_SUPPORTED_EXCEPTION = type("System.NotSupportedException");
  public static final Symbol READ_ONLY_DICTIONARY =
      type("System.Collections.ObjectModel.ReadOnlyDictionary");
  public static final Symbol STREAM = type("System.IO.Stream");
  public static final Symbol STRING_COMPARER = type("System.StringComparer");
  public static final Symbol TASK = type("System.Threading.Tasks.Task");
  public static final Symbol URI = type("System.Uri");

  // NSmithy models and serialization
  public static final Symbol DOCUMENT = type("NSmithy.Core.Document");
  public static final Symbol I_SMITHY_RETRYABLE_ERROR = type("NSmithy.Core.ISmithyRetryableError");
  public static final Symbol I_STRING_ENUM_VALUE = type("NSmithy.Core.Serde.IStringEnumValue");
  public static final Symbol I_STRUCT_MEMBER_WRITER =
      type("NSmithy.Core.Serde.IStructMemberWriter");
  public static final Symbol I_STRUCT_VALUE_SERIALIZER =
      type("NSmithy.Core.Serde.IStructValueSerializer");
  public static final Symbol MISSING_REQUIRED_MEMBER_EXCEPTION =
      type("NSmithy.Core.Serde.MissingRequiredMemberException");
  public static final Symbol OPERATION_SCHEMA = type("NSmithy.Core.Serde.OperationSchema");
  public static final Symbol SCHEMA = type("NSmithy.Core.Serde.Schema");
  public static final Symbol SCHEMAS = type("NSmithy.Core.Serde.Schemas");
  public static final Symbol SERVICE_SCHEMA = type("NSmithy.Core.Serde.ServiceSchema");
  public static final Symbol SHAPE_ID = type("NSmithy.Core.ShapeId");
  public static final Symbol SMITHY_UNIT = type("NSmithy.Core.SmithyUnit");
  public static final Symbol TRAIT = type("NSmithy.Core.Trait");
  public static final Symbol VALIDATION_EXCEPTION =
      type("NSmithy.Core.Validation.ValidationException");

  // NSmithy clients and HTTP
  public static final Symbol I_SERVER_OPERATION_PROTOCOL =
      type("NSmithy.Http.IServerOperationProtocol");
  public static final Symbol I_SERVICE_PROTOCOL = type("NSmithy.Http.IServiceProtocol");
  public static final Symbol SMITHY_CLIENT_CONFIG = type("NSmithy.Client.SmithyClientConfig");
  public static final Symbol SMITHY_CLIENT_RUNTIME = type("NSmithy.Client.SmithyClientRuntime");
  public static final Symbol SMITHY_HOST_LABEL = type("NSmithy.Client.SmithyHostLabel");
  public static final Symbol SMITHY_HOST_PREFIX = type("NSmithy.Client.SmithyHostPrefix");
  public static final Symbol SMITHY_HTTP_CLIENT_ENVIRONMENT =
      type("NSmithy.Client.SmithyHttpClientEnvironment");
  public static final Symbol SMITHY_HTTP_VERSION_PREFERENCE =
      type("NSmithy.Http.SmithyHttpVersionPreference");
  public static final Symbol SMITHY_OPERATION_BINDING =
      type("NSmithy.Client.SmithyOperationBinding");

  // NSmithy servers
  public static final Symbol I_SERVICE_DEFINITION = type("NSmithy.Server.IServiceDefinition");
  public static final Symbol OPERATION_JSON_SCHEMAS = type("NSmithy.Server.OperationJsonSchemas");
  public static final Symbol SERVICE_OPERATION = type("NSmithy.Server.ServiceOperation");
  public static final Symbol SERVICE_OPERATION_CATALOG =
      type("NSmithy.Server.ServiceOperationCatalog");
  public static final Symbol SERVICE_PROMPT_ARGUMENT_DEFINITION =
      type("NSmithy.Server.ServicePromptArgumentDefinition");
  public static final Symbol SERVICE_PROMPT_DEFINITION =
      type("NSmithy.Server.ServicePromptDefinition");
  public static final Symbol SMITHY_ASP_NET_CORE_HOST =
      type("NSmithy.Server.AspNetCore.SmithyAspNetCoreHost");
  public static final Symbol SMITHY_SERVER_RUNTIME = type("NSmithy.Server.SmithyServerRuntime");

  // NSmithy protocols and AWS support
  public static final Symbol AWS_JSON10_PROTOCOL =
      type("NSmithy.Protocols.AwsJson.AwsJson10Protocol");
  public static final Symbol AWS_JSON11_PROTOCOL =
      type("NSmithy.Protocols.AwsJson.AwsJson11Protocol");
  public static final Symbol AWS_QUERY_PROTOCOL =
      type("NSmithy.Protocols.AwsQuery.AwsQueryProtocol");
  public static final Symbol EC2_QUERY_PROTOCOL =
      type("NSmithy.Protocols.AwsQuery.Ec2QueryProtocol");
  public static final Symbol GLACIER_INTERCEPTOR = type("NSmithy.Aws.GlacierInterceptor");
  public static final Symbol GRPC_PROTOCOL = type("NSmithy.Protocols.Grpc.GrpcProtocol");
  public static final Symbol REST_JSON1_PROTOCOL =
      type("NSmithy.Protocols.RestJson.RestJson1Protocol");
  public static final Symbol REST_XML_PROTOCOL = type("NSmithy.Protocols.RestXml.RestXmlProtocol");
  public static final Symbol RPC_V2_CBOR_PROTOCOL =
      type("NSmithy.Protocols.RpcV2Cbor.RpcV2CborProtocol");
  public static final Symbol SIMPLE_REST_JSON_PROTOCOL =
      type("NSmithy.Protocols.RestJson.SimpleRestJsonProtocol");

  // ASP.NET Core and dependency injection
  public static final Symbol FROM_SERVICES_ATTRIBUTE =
      type("Microsoft.AspNetCore.Mvc.FromServicesAttribute");
  public static final Symbol HTTP_CONTEXT = type("Microsoft.AspNetCore.Http.HttpContext");
  public static final Symbol I_ENDPOINT_ROUTE_BUILDER =
      type("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder");
  public static final Symbol I_HTTP_CLIENT_BUILDER =
      type("Microsoft.Extensions.DependencyInjection.IHttpClientBuilder");
  public static final Symbol I_SERVICE_COLLECTION =
      type("Microsoft.Extensions.DependencyInjection.IServiceCollection");
  public static final Symbol SERVICE_DESCRIPTOR =
      type("Microsoft.Extensions.DependencyInjection.ServiceDescriptor");

  // Namespace imports required for extension-method lookup.
  public static final String MS_EXT_DI = "Microsoft.Extensions.DependencyInjection";
  public static final String MS_EXT_DI_EXTENSIONS =
      "Microsoft.Extensions.DependencyInjection.Extensions";
  public static final String MS_ASPNETCORE_BUILDER = "Microsoft.AspNetCore.Builder";

  public static final String NSMITHY_SERVER_ASPNETCORE = "NSmithy.Server.AspNetCore";

  private RuntimeTypes() {}

  private static Symbol type(String qualifiedName) {
    int separator = qualifiedName.lastIndexOf('.');
    return Symbol.builder()
        .name(qualifiedName.substring(separator + 1))
        .namespace(qualifiedName.substring(0, separator), ".")
        .build();
  }
}

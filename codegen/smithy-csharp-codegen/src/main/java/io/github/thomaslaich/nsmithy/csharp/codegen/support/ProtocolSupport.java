/*
 * Encapsulates protocol-specific decisions for HTTP client codegen:
 *   - which runtime helper class to call (RestJsonClientProtocol, RestXmlClientProtocol, RpcV2CborClientProtocol)
 *   - which codec to use (JSON / XML / CBOR)
 *   - whether to use HTTP binding traits or treat the whole input/output as the body
 *   - how to dispatch errors (status code vs error type from body)
 *   - URI scheme (real @http vs synthetic /service/X/operation/Y for rpcv2)
 *
 * Mirrors the protocol-switching helpers in CSharpShapeGenerator.ClientEmitter.cs.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import java.util.ArrayList;
import java.util.List;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ProtocolSupport {

  public enum Kind {
    AWS_JSON_1_0,
    AWS_JSON_1_1,
    SIMPLE_REST_JSON,
    REST_JSON_1,
    REST_XML,
    RPC_V2_CBOR,
    GRPC
  }

  private ProtocolSupport() {}

  public static boolean isRestJson1Service(ServiceShape s) {
    return s.findTrait(TraitIds.REST_JSON_1).isPresent();
  }

  public static boolean isAwsJson10Service(ServiceShape s) {
    return s.findTrait(TraitIds.AWS_JSON_1_0).isPresent();
  }

  public static boolean isAwsJson11Service(ServiceShape s) {
    return s.findTrait(TraitIds.AWS_JSON_1_1).isPresent();
  }

  public static boolean isSimpleRestJsonService(ServiceShape s) {
    return s.findTrait(TraitIds.SIMPLE_REST_JSON).isPresent();
  }

  public static boolean isRestXmlService(ServiceShape s) {
    return s.findTrait(TraitIds.REST_XML).isPresent();
  }

  public static boolean isRpcV2CborService(ServiceShape s) {
    return s.findTrait(TraitIds.RPC_V2_CBOR).isPresent();
  }

  public static boolean isGrpcService(ServiceShape s) {
    return s.findTrait(TraitIds.GRPC).isPresent();
  }

  public static boolean emitsAspNetCoreServer(ServiceShape s) {
    return emitsHttpAspNetCoreServer(s) || isGrpcService(s);
  }

  public static boolean emitsHttpAspNetCoreServer(ServiceShape s) {
    return isSimpleRestJsonService(s) || isRestJson1Service(s) || isRpcV2CborService(s);
  }

  public static Kind kindOf(ServiceShape s) {
    if (isRpcV2CborService(s)) return Kind.RPC_V2_CBOR;
    if (isRestXmlService(s)) return Kind.REST_XML;
    if (isAwsJson11Service(s)) return Kind.AWS_JSON_1_1;
    if (isAwsJson10Service(s)) return Kind.AWS_JSON_1_0;
    if (isSimpleRestJsonService(s)) return Kind.SIMPLE_REST_JSON;
    if (isGrpcService(s)) return Kind.GRPC;
    return Kind.REST_JSON_1;
  }

  /**
   * Every protocol the service declares, in the documented precedence order {@code rpcv2Cbor >
   * restXml > awsJson1_1 > awsJson1_0 > simpleRestJson > restJson1 > grpc}. The unified client
   * builder generates a {@code With{Kind}()} method per declared kind and uses the first as the
   * default protocol. Returns an empty list for a service with no supported protocol trait.
   */
  public static List<Kind> declaredKinds(ServiceShape s) {
    List<Kind> kinds = new ArrayList<>();
    if (isRpcV2CborService(s)) kinds.add(Kind.RPC_V2_CBOR);
    if (isRestXmlService(s)) kinds.add(Kind.REST_XML);
    if (isAwsJson11Service(s)) kinds.add(Kind.AWS_JSON_1_1);
    if (isAwsJson10Service(s)) kinds.add(Kind.AWS_JSON_1_0);
    if (isSimpleRestJsonService(s)) kinds.add(Kind.SIMPLE_REST_JSON);
    if (isRestJson1Service(s)) kinds.add(Kind.REST_JSON_1);
    if (isGrpcService(s)) kinds.add(Kind.GRPC);
    return kinds;
  }

  /** The default protocol for the client: the first declared kind by precedence. */
  public static Kind primaryKind(ServiceShape s) {
    List<Kind> kinds = declaredKinds(s);
    if (kinds.isEmpty()) {
      throw new IllegalStateException(
          "Service " + s.getId() + " declares no supported protocol trait");
    }
    return kinds.get(0);
  }

  /** Protocol helper class for the given protocol. */
  public static String protocolType(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0 -> "AwsJson10Protocol";
      case AWS_JSON_1_1 -> "AwsJson11Protocol";
      case SIMPLE_REST_JSON -> "SimpleRestJsonProtocol";
      case REST_JSON_1 -> "RestJson1Protocol";
      case REST_XML -> "RestXmlProtocol";
      case RPC_V2_CBOR -> "RpcV2CborProtocol";
      case GRPC -> "GrpcProtocol";
    };
  }

  public static String mediaType(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0 -> "application/x-amz-json-1.0";
      case AWS_JSON_1_1 -> "application/x-amz-json-1.1";
      case SIMPLE_REST_JSON, REST_JSON_1 -> "application/json";
      case REST_XML -> "application/xml";
      case RPC_V2_CBOR -> "application/cbor";
      case GRPC -> "application/grpc+proto";
    };
  }

  /** Runtime namespace housing the protocol class. */
  public static String runtimeProtocolNamespace(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0, AWS_JSON_1_1 -> RuntimeTypes.NSMITHY_PROTOCOLS_AWSJSON;
      case SIMPLE_REST_JSON, REST_JSON_1 -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTJSON;
      case REST_XML -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTXML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_PROTOCOLS_RPCV2CBOR;
      case GRPC -> RuntimeTypes.NSMITHY_PROTOCOLS_GRPC;
    };
  }

  /** Runtime namespace housing the codec singleton. */
  public static String codecNamespace(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0, AWS_JSON_1_1 -> RuntimeTypes.NSMITHY_CODECS_JSON;
      case SIMPLE_REST_JSON, REST_JSON_1 -> RuntimeTypes.NSMITHY_CODECS_JSON;
      case REST_XML -> RuntimeTypes.NSMITHY_CODECS_XML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_CODECS_CBOR;
      case GRPC -> RuntimeTypes.NSMITHY_CODECS_PROTO;
    };
  }
}

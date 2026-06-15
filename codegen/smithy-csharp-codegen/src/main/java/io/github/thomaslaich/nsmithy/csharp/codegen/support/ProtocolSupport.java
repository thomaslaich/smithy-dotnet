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
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ProtocolSupport {

  public enum Kind {
    REST_JSON,
    REST_XML,
    RPC_V2_CBOR
  }

  private ProtocolSupport() {}

  public static boolean isRestJsonService(ServiceShape s) {
    return s.findTrait(TraitIds.SIMPLE_REST_JSON).isPresent()
        || s.findTrait(TraitIds.REST_JSON_1).isPresent();
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

  public static boolean emitsHttpClient(ServiceShape s) {
    return isRestJsonService(s) || isRestXmlService(s) || isRpcV2CborService(s);
  }

  public static boolean emitsAspNetCoreServer(ServiceShape s) {
    return isRestJsonService(s) || isRpcV2CborService(s);
  }

  public static Kind kindOf(ServiceShape s) {
    if (isRpcV2CborService(s)) return Kind.RPC_V2_CBOR;
    if (isRestXmlService(s)) return Kind.REST_XML;
    return Kind.REST_JSON;
  }

  /** Protocol helper class for the given protocol. */
  public static String protocolType(Kind kind) {
    return switch (kind) {
      case REST_JSON -> "RestJsonProtocol";
      case REST_XML -> "RestXmlProtocol";
      case RPC_V2_CBOR -> "RpcV2CborProtocol";
    };
  }

  /** True for REST protocols whose HTTP bindings are handled by RestProtocol. */
  public static boolean usesRestBindings(Kind kind) {
    return kind == Kind.REST_JSON || kind == Kind.REST_XML;
  }

  public static String mediaType(Kind kind) {
    return switch (kind) {
      case REST_JSON -> "application/json";
      case REST_XML -> "application/xml";
      case RPC_V2_CBOR -> "application/cbor";
    };
  }

  /** Runtime namespace housing the protocol class. */
  public static String runtimeProtocolNamespace(Kind kind) {
    return switch (kind) {
      case REST_JSON -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTJSON;
      case REST_XML -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTXML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_PROTOCOLS_RPCV2CBOR;
    };
  }

  /** Runtime namespace housing the codec singleton. */
  public static String codecNamespace(Kind kind) {
    return switch (kind) {
      case REST_JSON -> RuntimeTypes.NSMITHY_CODECS_JSON;
      case REST_XML -> RuntimeTypes.NSMITHY_CODECS_XML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_CODECS_CBOR;
    };
  }
}

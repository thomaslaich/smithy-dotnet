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
package io.github.thomaslaich.smithy.csharp.codegen.support;

import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.TraitIds;
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
    // Currently only simpleRestJson on the server.
    return s.findTrait(TraitIds.SIMPLE_REST_JSON).isPresent();
  }

  public static Kind kindOf(ServiceShape s) {
    if (isRpcV2CborService(s)) return Kind.RPC_V2_CBOR;
    if (isRestXmlService(s)) return Kind.REST_XML;
    return Kind.REST_JSON;
  }

  /** C# class name of the protocol runtime helper. */
  public static String runtimeProtocolType(Kind kind) {
    return switch (kind) {
      case REST_JSON -> "RestJsonClientProtocol";
      case REST_XML -> "RestXmlClientProtocol";
      case RPC_V2_CBOR -> "RpcV2CborClientProtocol";
    };
  }

  /** Static expression yielding the document codec singleton. */
  public static String codecExpression(Kind kind) {
    return switch (kind) {
      case REST_JSON -> "SmithyJsonPayloadCodec.Default";
      case REST_XML -> "SmithyXmlPayloadCodec.Default";
      case RPC_V2_CBOR -> "SmithyCborPayloadCodec.Default";
    };
  }

  /** Runtime namespace housing the protocol class. */
  public static String runtimeProtocolNamespace(Kind kind) {
    return switch (kind) {
      case REST_JSON -> RuntimeTypes.NSMITHY_CLIENT_RESTJSON;
      case REST_XML -> RuntimeTypes.NSMITHY_CLIENT_RESTXML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_CLIENT_RPCV2CBOR;
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

  /**
   * True when the entire request/response body is treated as a single document (no @httpLabel /
   *
   * @httpHeader / etc. binding splitting). RestXml and RpcV2Cbor follow this style.
   */
  public static boolean useDocumentBindings(Kind kind) {
    return kind != Kind.REST_JSON;
  }
}

/*
 * Encapsulates protocol-specific decisions for HTTP client codegen:
 *   - which runtime helper class to call (REST, AWS JSON/Query, rpcv2Cbor, and gRPC protocols)
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
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ProtocolSupport {

  public enum Kind {
    AWS_JSON_1_0,
    AWS_JSON_1_1,
    AWS_QUERY,
    EC2_QUERY,
    SIMPLE_REST_JSON,
    REST_JSON_1,
    REST_XML,
    RPC_V2_CBOR,
    GRPC
  }

  public record HttpVersionPreference(String alpnId, boolean allowDowngrade) {}

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

  public static boolean isAwsQueryService(ServiceShape s) {
    return s.findTrait(TraitIds.AWS_QUERY).isPresent();
  }

  public static boolean isEc2QueryService(ServiceShape s) {
    return s.findTrait(TraitIds.EC2_QUERY).isPresent();
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
    if (isAwsQueryService(s)) return Kind.AWS_QUERY;
    if (isEc2QueryService(s)) return Kind.EC2_QUERY;
    if (isAwsJson11Service(s)) return Kind.AWS_JSON_1_1;
    if (isAwsJson10Service(s)) return Kind.AWS_JSON_1_0;
    if (isSimpleRestJsonService(s)) return Kind.SIMPLE_REST_JSON;
    if (isGrpcService(s)) return Kind.GRPC;
    return Kind.REST_JSON_1;
  }

  /**
   * Every protocol the service declares, in the documented precedence order {@code rpcv2Cbor >
   * restXml > awsQuery > ec2Query > awsJson1_1 > awsJson1_0 > simpleRestJson > restJson1 > grpc}.
   * The unified client builder generates a {@code With{Kind}()} method per declared kind and uses
   * the first as the default protocol. Returns an empty list for a service with no supported
   * protocol trait.
   */
  public static List<Kind> declaredKinds(ServiceShape s) {
    List<Kind> kinds = new ArrayList<>();
    if (isRpcV2CborService(s)) kinds.add(Kind.RPC_V2_CBOR);
    if (isRestXmlService(s)) kinds.add(Kind.REST_XML);
    if (isAwsQueryService(s)) kinds.add(Kind.AWS_QUERY);
    if (isEc2QueryService(s)) kinds.add(Kind.EC2_QUERY);
    if (isAwsJson11Service(s)) kinds.add(Kind.AWS_JSON_1_1);
    if (isAwsJson10Service(s)) kinds.add(Kind.AWS_JSON_1_0);
    if (isSimpleRestJsonService(s)) kinds.add(Kind.SIMPLE_REST_JSON);
    if (isRestJson1Service(s)) kinds.add(Kind.REST_JSON_1);
    if (isGrpcService(s)) kinds.add(Kind.GRPC);
    return kinds;
  }

  public static ShapeId traitId(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0 -> TraitIds.AWS_JSON_1_0;
      case AWS_JSON_1_1 -> TraitIds.AWS_JSON_1_1;
      case AWS_QUERY -> TraitIds.AWS_QUERY;
      case EC2_QUERY -> TraitIds.EC2_QUERY;
      case SIMPLE_REST_JSON -> TraitIds.SIMPLE_REST_JSON;
      case REST_JSON_1 -> TraitIds.REST_JSON_1;
      case REST_XML -> TraitIds.REST_XML;
      case RPC_V2_CBOR -> TraitIds.RPC_V2_CBOR;
      case GRPC -> TraitIds.GRPC;
    };
  }

  /** Reads the selected protocol trait's ordered ALPN preferences, when modeled. */
  public static Optional<HttpVersionPreference> httpVersionPreference(
      ServiceShape service, Kind kind, boolean eventStream) {
    var trait = service.findTrait(traitId(kind));
    if (trait.isEmpty() || !trait.get().toNode().isObjectNode()) {
      return Optional.empty();
    }

    var object = trait.get().toNode().expectObjectNode();
    Optional<ArrayNode> values =
        eventStream ? object.getArrayMember("eventStreamHttp") : Optional.empty();
    if (values.isEmpty()) {
      values = object.getArrayMember("http");
    }
    if (values.isEmpty() || values.get().isEmpty()) {
      return Optional.empty();
    }

    List<String> ids =
        values.get().getElements().stream()
            .map(node -> node.expectStringNode().getValue())
            .toList();
    int selectedIndex = -1;
    int selectedRank = -1;
    for (int i = 0; i < ids.size(); i++) {
      int rank = httpVersionRank(ids.get(i));
      if (rank >= 0) {
        selectedIndex = i;
        selectedRank = rank;
        break;
      }
    }
    if (selectedIndex < 0) {
      throw new CodegenException(
          "Protocol "
              + traitId(kind)
              + " on service "
              + service.getId()
              + " has no supported HTTP ALPN id in "
              + ids
              + ". Supported ids: h3, h2, http/1.1.");
    }

    boolean allowDowngrade = false;
    for (int i = selectedIndex + 1; i < ids.size(); i++) {
      int rank = httpVersionRank(ids.get(i));
      if (rank >= 0 && rank < selectedRank) {
        allowDowngrade = true;
        break;
      }
    }
    return Optional.of(new HttpVersionPreference(ids.get(selectedIndex), allowDowngrade));
  }

  public static boolean hasEventStreamOperations(Model model, ServiceShape service) {
    return TopDownIndex.of(model).getContainedOperations(service).stream()
        .anyMatch(
            operation ->
                ShapeSupport.isEventStreamShape(model, operation.getInputShape())
                    || ShapeSupport.isEventStreamShape(model, operation.getOutputShape()));
  }

  private static int httpVersionRank(String alpnId) {
    return switch (alpnId) {
      case "http/1.1" -> 1;
      case "h2" -> 2;
      case "h3" -> 3;
      default -> -1;
    };
  }

  /** The default protocol for the client: the first declared kind by precedence. */
  public static Kind primaryKind(ServiceShape s) {
    List<Kind> kinds = declaredKinds(s);
    if (kinds.isEmpty()) {
      throw new CodegenException(
          "Service "
              + s.getId()
              + " declares no supported protocol trait. Supported protocol traits: "
              + String.join(
                  ", ",
                  TraitIds.RPC_V2_CBOR.toString(),
                  TraitIds.REST_XML.toString(),
                  TraitIds.AWS_QUERY.toString(),
                  TraitIds.EC2_QUERY.toString(),
                  TraitIds.AWS_JSON_1_1.toString(),
                  TraitIds.AWS_JSON_1_0.toString(),
                  TraitIds.SIMPLE_REST_JSON.toString(),
                  TraitIds.REST_JSON_1.toString(),
                  TraitIds.GRPC.toString())
              + ".");
    }
    return kinds.get(0);
  }

  /** Protocol helper class for the given protocol. */
  public static String protocolType(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0 -> "AwsJson10Protocol";
      case AWS_JSON_1_1 -> "AwsJson11Protocol";
      case AWS_QUERY -> "AwsQueryProtocol";
      case EC2_QUERY -> "Ec2QueryProtocol";
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
      case AWS_QUERY, EC2_QUERY -> "application/x-www-form-urlencoded";
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
      case AWS_QUERY, EC2_QUERY -> RuntimeTypes.NSMITHY_PROTOCOLS_AWSQUERY;
      case SIMPLE_REST_JSON, REST_JSON_1 -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTJSON;
      case REST_XML -> RuntimeTypes.NSMITHY_PROTOCOLS_RESTXML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_PROTOCOLS_RPCV2CBOR;
      case GRPC -> RuntimeTypes.NSMITHY_PROTOCOLS_GRPC;
    };
  }

  /** True when the runtime protocol can bind Smithy event-stream operation inputs/outputs. */
  public static boolean supportsEventStreams(Kind kind) {
    return switch (kind) {
      case REST_JSON_1, SIMPLE_REST_JSON, RPC_V2_CBOR, GRPC -> true;
      case AWS_JSON_1_0, AWS_JSON_1_1, AWS_QUERY, EC2_QUERY, REST_XML -> false;
    };
  }

  /** Runtime namespace housing the codec singleton. */
  public static String codecNamespace(Kind kind) {
    return switch (kind) {
      case AWS_JSON_1_0, AWS_JSON_1_1 -> RuntimeTypes.NSMITHY_CODECS_JSON;
      case AWS_QUERY, EC2_QUERY -> RuntimeTypes.NSMITHY_CODECS_XML;
      case SIMPLE_REST_JSON, REST_JSON_1 -> RuntimeTypes.NSMITHY_CODECS_JSON;
      case REST_XML -> RuntimeTypes.NSMITHY_CODECS_XML;
      case RPC_V2_CBOR -> RuntimeTypes.NSMITHY_CODECS_CBOR;
      case GRPC -> RuntimeTypes.NSMITHY_CODECS_PROTO;
    };
  }
}

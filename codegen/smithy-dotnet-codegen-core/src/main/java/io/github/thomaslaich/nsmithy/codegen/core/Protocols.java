package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

/** Which protocols a service's client and server use. */
@SmithyInternalApi
public final class Protocols {

  /** The built-in protocols, in the order a service's protocols are listed. */
  public static final List<DotnetProtocol> BUILT_IN =
      List.of(
          new DotnetProtocol(
              TraitIds.RPC_V2_CBOR,
              "RpcV2Cbor",
              "NSmithy.Protocols.RpcV2Cbor.RpcV2CborProtocol",
              true,
              true),
          new DotnetProtocol(
              TraitIds.RPC_V2_JSON,
              "RpcV2Json",
              "NSmithy.Protocols.RpcV2Json.RpcV2JsonProtocol",
              true,
              true),
          new DotnetProtocol(
              TraitIds.SIMPLE_REST_JSON,
              "SimpleRestJson",
              "NSmithy.Protocols.RestJson.SimpleRestJsonProtocol",
              true,
              true),
          new DotnetProtocol(
              TraitIds.REST_JSON_1,
              "RestJson1",
              "NSmithy.Protocols.RestJson.RestJson1Protocol",
              true,
              true),
          new DotnetProtocol(
              TraitIds.GRPC, "Grpc", "NSmithy.Protocols.Grpc.GrpcProtocol", true, true));

  /** The HTTP version a protocol trait prefers, as an ALPN id, and whether it may fall back. */
  public record HttpVersionPreference(String alpnId, boolean allowDowngrade) {}

  private Protocols() {}

  /** Checks that no two of {@code protocols} register the same trait, and returns them. */
  public static <P extends ProtocolInfo> List<P> registered(List<P> protocols) {
    Map<ShapeId, P> byTrait = new HashMap<>();
    for (P protocol : protocols) {
      P existing = byTrait.putIfAbsent(protocol.trait(), protocol);
      if (existing != null) {
        throw new CodegenException(
            "Protocol "
                + protocol.trait()
                + " is registered twice, as "
                + existing.runtimeTypeName()
                + " and "
                + protocol.runtimeTypeName()
                + ".");
      }
    }
    return protocols;
  }

  /**
   * The registered protocols {@code service} carries, in registration order. Two of them with the
   * same name fail code generation, since the name is a generated identifier.
   */
  public static <P extends ProtocolInfo> List<P> ofService(
      List<P> registered, ServiceShape service) {
    List<P> protocols =
        registered(new ArrayList<>(registered)).stream()
            .filter(protocol -> service.findTrait(protocol.trait()).isPresent())
            .toList();
    Map<String, P> byName = new HashMap<>();
    for (P protocol : protocols) {
      P existing = byName.putIfAbsent(protocol.name(), protocol);
      if (existing != null) {
        throw new CodegenException(
            "Protocols "
                + existing.trait()
                + " and "
                + protocol.trait()
                + " of service "
                + service.getId()
                + " share the name '"
                + protocol.name()
                + "'.");
      }
    }
    return protocols;
  }

  /**
   * The client's default protocol: the configured one, or else the service's first. Empty for a
   * service with no registered protocol.
   */
  public static <P extends ProtocolInfo> Optional<P> clientDefault(
      List<P> registered, Optional<ShapeId> configured, ServiceShape service) {
    List<P> protocols = ofService(registered, service);
    if (configured.isEmpty()) {
      return protocols.stream().findFirst();
    }
    return Optional.of(
        protocols.stream()
            .filter(protocol -> protocol.trait().equals(configured.get()))
            .findFirst()
            .orElseThrow(
                () ->
                    new CodegenException(
                        "defaultProtocol "
                            + configured.get()
                            + " is not a protocol of service "
                            + service.getId()
                            + ". Its protocols: "
                            + protocols.stream().map(p -> p.trait().toString()).toList()
                            + ".")));
  }

  /** Reads the protocol trait's ordered ALPN preferences, when modeled. */
  public static Optional<HttpVersionPreference> httpVersionPreference(
      ServiceShape service, ShapeId protocolTrait, boolean eventStream) {
    var trait = service.findTrait(protocolTrait);
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
              + protocolTrait
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
        .anyMatch(operation -> ShapeSupport.isEventStreamOperation(model, operation));
  }

  private static int httpVersionRank(String alpnId) {
    return switch (alpnId) {
      case "http/1.1" -> 1;
      case "h2" -> 2;
      case "h3" -> 3;
      default -> -1;
    };
  }
}

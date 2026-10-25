/*
 * The request/response protocols code generation knows: the built-in ones and those integrations
 * supply through CSharpIntegration.protocols(). Generators work from a service's matched protocols
 * and name no protocol themselves.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.codegen.core.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
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

@SmithyInternalApi
public final class ProtocolSupport {

  /** The built-in protocols, in the order a service's protocols are listed. */
  public static final List<CSharpProtocol> BUILT_IN =
      List.of(
          new CSharpProtocol(
              TraitIds.RPC_V2_CBOR, "RpcV2Cbor", RuntimeTypes.RPC_V2_CBOR_PROTOCOL, true, true),
          new CSharpProtocol(
              TraitIds.RPC_V2_JSON, "RpcV2Json", RuntimeTypes.RPC_V2_JSON_PROTOCOL, true, true),
          new CSharpProtocol(
              TraitIds.SIMPLE_REST_JSON,
              "SimpleRestJson",
              RuntimeTypes.SIMPLE_REST_JSON_PROTOCOL,
              true,
              true),
          new CSharpProtocol(
              TraitIds.REST_JSON_1, "RestJson1", RuntimeTypes.REST_JSON1_PROTOCOL, true, true),
          new CSharpProtocol(TraitIds.GRPC, "Grpc", RuntimeTypes.GRPC_PROTOCOL, true, true));

  public record HttpVersionPreference(String alpnId, boolean allowDowngrade) {}

  /**
   * Every registered protocol: the built-in ones, then each integration's in integration order. Two
   * registrations for one trait fail code generation.
   */
  public static List<CSharpProtocol> registered(List<CSharpIntegration> integrations) {
    List<CSharpProtocol> protocols = new ArrayList<>(BUILT_IN);
    for (CSharpIntegration integration : integrations) {
      protocols.addAll(integration.protocols());
    }

    Map<ShapeId, CSharpProtocol> byTrait = new HashMap<>();
    for (CSharpProtocol protocol : protocols) {
      CSharpProtocol existing = byTrait.putIfAbsent(protocol.trait(), protocol);
      if (existing != null) {
        throw new CodegenException(
            "Protocol "
                + protocol.trait()
                + " is registered twice, as "
                + existing.runtimeType()
                + " and "
                + protocol.runtimeType()
                + ".");
      }
    }
    return protocols;
  }

  /**
   * The protocols {@code service} carries, in registration order. Two of them with the same name
   * fail code generation, since the name is a generated identifier.
   */
  public static List<CSharpProtocol> protocols(GenerationContext context, ServiceShape service) {
    return protocols(context.integrations(), service);
  }

  public static List<CSharpProtocol> protocols(
      List<CSharpIntegration> integrations, ServiceShape service) {
    List<CSharpProtocol> protocols =
        registered(integrations).stream()
            .filter(protocol -> service.findTrait(protocol.trait()).isPresent())
            .toList();

    Map<String, CSharpProtocol> byName = new HashMap<>();
    for (CSharpProtocol protocol : protocols) {
      CSharpProtocol existing = byName.putIfAbsent(protocol.name(), protocol);
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

  /** The service's protocols that serve requests, in registration order. */
  public static List<CSharpProtocol> serverProtocols(
      GenerationContext context, ServiceShape service) {
    return protocols(context, service).stream().filter(CSharpProtocol::server).toList();
  }

  /**
   * The client's default protocol: the one the {@code defaultProtocol} setting names, or else the
   * service's first. Empty for a service with no registered protocol.
   */
  public static Optional<CSharpProtocol> clientDefault(
      GenerationContext context, ServiceShape service) {
    return clientDefault(context.integrations(), context.settings().defaultProtocol(), service);
  }

  public static Optional<CSharpProtocol> clientDefault(
      List<CSharpIntegration> integrations, Optional<ShapeId> configured, ServiceShape service) {
    List<CSharpProtocol> protocols = protocols(integrations, service);
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

  /** Reads the selected protocol trait's ordered ALPN preferences, when modeled. */
  public static Optional<HttpVersionPreference> httpVersionPreference(
      ServiceShape service, CSharpProtocol protocol, boolean eventStream) {
    var trait = service.findTrait(protocol.trait());
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
              + protocol.trait()
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

  private ProtocolSupport() {}

  private static int httpVersionRank(String alpnId) {
    return switch (alpnId) {
      case "http/1.1" -> 1;
      case "h2" -> 2;
      case "h3" -> 3;
      default -> -1;
    };
  }
}

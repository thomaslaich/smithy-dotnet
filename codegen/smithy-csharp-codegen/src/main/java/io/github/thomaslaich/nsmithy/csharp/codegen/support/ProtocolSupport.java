/*
 * The request/response protocols code generation knows: the built-in ones and those integrations
 * supply through CSharpIntegration.protocols(). Generators work from a service's matched protocols
 * and name no protocol themselves.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import io.github.thomaslaich.nsmithy.codegen.core.Protocols;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ProtocolSupport {

  /** The built-in protocols, in the order a service's protocols are listed. */
  public static final List<CSharpProtocol> BUILT_IN =
      Protocols.BUILT_IN.stream().map(CSharpProtocol::of).toList();

  /**
   * Every registered protocol: the built-in ones, then each integration's in integration order. Two
   * registrations for one trait fail code generation.
   */
  public static List<CSharpProtocol> registered(List<CSharpIntegration> integrations) {
    List<CSharpProtocol> protocols = new ArrayList<>(BUILT_IN);
    for (CSharpIntegration integration : integrations) {
      protocols.addAll(integration.protocols());
    }
    return Protocols.registered(protocols);
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
    return Protocols.ofService(registered(integrations), service);
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
    return Protocols.clientDefault(registered(integrations), configured, service);
  }

  /** Reads the selected protocol trait's ordered ALPN preferences, when modeled. */
  public static Optional<Protocols.HttpVersionPreference> httpVersionPreference(
      ServiceShape service, CSharpProtocol protocol, boolean eventStream) {
    return Protocols.httpVersionPreference(service, protocol.trait(), eventStream);
  }

  public static boolean hasEventStreamOperations(Model model, ServiceShape service) {
    return Protocols.hasEventStreamOperations(model, service);
  }

  private ProtocolSupport() {}
}

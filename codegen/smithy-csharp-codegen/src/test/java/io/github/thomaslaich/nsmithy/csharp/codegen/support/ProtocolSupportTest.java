package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
import java.util.List;
import java.util.Optional;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DynamicTrait;

final class ProtocolSupportTest {

  private static final ShapeId MSGPACK = ShapeId.from("acme.protocols#msgpack");

  @Test
  void protocolsFollowRegistrationOrder() {
    var service = service(TraitIds.GRPC, TraitIds.RPC_V2_JSON, TraitIds.RPC_V2_CBOR);

    assertEquals(
        List.of("RpcV2Cbor", "RpcV2Json", "Grpc"),
        ProtocolSupport.protocols(List.of(), service).stream().map(CSharpProtocol::name).toList());
    assertEquals(
        "NSmithy.Protocols.RpcV2Cbor.RpcV2CborProtocol",
        ProtocolSupport.clientDefault(List.of(), Optional.empty(), service)
            .orElseThrow()
            .runtimeType()
            .getFullName());
  }

  @Test
  void integrationProtocolsFollowTheBuiltInOnes() {
    var service = service(MSGPACK, TraitIds.REST_JSON_1);

    assertEquals(
        List.of("RestJson1", "MsgPack"),
        ProtocolSupport.protocols(List.of(integration(msgpack("MsgPack"))), service).stream()
            .map(CSharpProtocol::name)
            .toList());
  }

  @Test
  void defaultProtocolSelectsTheClientDefault() {
    var service = service(TraitIds.RPC_V2_CBOR, TraitIds.REST_JSON_1);

    assertEquals(
        TraitIds.REST_JSON_1,
        ProtocolSupport.clientDefault(List.of(), Optional.of(TraitIds.REST_JSON_1), service)
            .orElseThrow()
            .trait());
  }

  @Test
  void defaultProtocolMustBeOneOfTheServicesProtocols() {
    var service = service(TraitIds.REST_JSON_1);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () ->
                ProtocolSupport.clientDefault(
                    List.of(), Optional.of(TraitIds.RPC_V2_CBOR), service));

    assertTrue(ex.getMessage().contains("smithy.protocols#rpcv2Cbor"), ex.getMessage());
  }

  @Test
  void aServiceWithoutProtocolsHasNoClientDefault() {
    assertTrue(ProtocolSupport.clientDefault(List.of(), Optional.empty(), service()).isEmpty());
  }

  @Test
  void aTraitRegisteredTwiceFails() {
    var duplicate =
        new CSharpProtocol(
            TraitIds.REST_JSON_1,
            "AcmeRestJson",
            CSharpProtocol.runtimeType("Acme.Protocols.RestJsonProtocol"),
            false,
            false);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () -> ProtocolSupport.registered(List.of(integration(duplicate))));

    assertTrue(ex.getMessage().contains("aws.protocols#restJson1"), ex.getMessage());
  }

  @Test
  void twoProtocolsOfAServiceWithOneNameFail() {
    var service = service(MSGPACK, TraitIds.REST_JSON_1);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () -> ProtocolSupport.protocols(List.of(integration(msgpack("RestJson1"))), service));

    assertTrue(ex.getMessage().contains("share the name 'RestJson1'"), ex.getMessage());
  }

  private static CSharpProtocol msgpack(String name) {
    return new CSharpProtocol(
        MSGPACK, name, CSharpProtocol.runtimeType("Acme.Protocols.MsgPackProtocol"), false, true);
  }

  private static CSharpIntegration integration(CSharpProtocol protocol) {
    return new CSharpIntegration() {
      @Override
      public String name() {
        return "test-" + protocol.name();
      }

      @Override
      public List<CSharpProtocol> protocols() {
        return List.of(protocol);
      }
    };
  }

  private static ServiceShape service(ShapeId... protocols) {
    var builder = ServiceShape.builder().id(ShapeId.from("example.weather#Weather")).version("1");
    for (ShapeId protocol : protocols) {
      builder.addTrait(new DynamicTrait(protocol, Node.objectNode()));
    }
    return builder.build();
  }
}

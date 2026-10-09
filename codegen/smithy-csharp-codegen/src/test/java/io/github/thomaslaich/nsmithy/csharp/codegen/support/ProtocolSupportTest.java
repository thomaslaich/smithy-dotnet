package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DynamicTrait;

final class ProtocolSupportTest {

  @Test
  void primaryKindDiagnosticNamesServiceAndSupportedProtocols() {
    var service =
        ServiceShape.builder().id(ShapeId.from("example.weather#Weather")).version("1").build();

    CodegenException ex =
        assertThrows(CodegenException.class, () -> ProtocolSupport.primaryKind(service));

    assertTrue(
        ex.getMessage()
            .contains("Service example.weather#Weather declares no supported protocol trait"),
        ex.getMessage());
    assertTrue(ex.getMessage().contains("aws.protocols#restJson1"), ex.getMessage());
    assertTrue(ex.getMessage().contains("smithy.protocols#rpcv2Cbor"), ex.getMessage());
  }

  @Test
  void rpcv2JsonServiceBindsTheJsonRuntimeProtocol() {
    var service = service(TraitIds.RPC_V2_JSON);

    assertEquals(Kind.RPC_V2_JSON, ProtocolSupport.kindOf(service));
    assertEquals(
        "NSmithy.Protocols.RpcV2Json.RpcV2JsonProtocol",
        ProtocolSupport.protocolType(Kind.RPC_V2_JSON).getFullName());
    assertEquals("application/json", ProtocolSupport.mediaType(Kind.RPC_V2_JSON));
    assertTrue(ProtocolSupport.supportsEventStreams(Kind.RPC_V2_JSON));
    assertTrue(ProtocolSupport.emitsHttpAspNetCoreServer(service));
  }

  @Test
  void rpcv2CborTakesPrecedenceOverRpcv2Json() {
    var service = service(TraitIds.RPC_V2_JSON, TraitIds.RPC_V2_CBOR);

    assertEquals(
        List.of(Kind.RPC_V2_CBOR, Kind.RPC_V2_JSON), ProtocolSupport.declaredKinds(service));
    assertEquals(Kind.RPC_V2_CBOR, ProtocolSupport.primaryKind(service));
  }

  private static ServiceShape service(ShapeId... protocols) {
    var builder = ServiceShape.builder().id(ShapeId.from("example.weather#Weather")).version("1");
    for (ShapeId protocol : protocols) {
      builder.addTrait(new DynamicTrait(protocol, Node.objectNode()));
    }
    return builder.build();
  }
}

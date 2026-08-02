package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

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
}

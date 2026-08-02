package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;

final class SchemaGeneratorTest {

  @Test
  void unsupportedPreludeSchemaDiagnosticNamesSupportedShapes() {
    var shape = StructureShape.builder().id(ShapeId.from("smithy.api#Unsupported")).build();

    CodegenException ex =
        assertThrows(
            CodegenException.class, () -> SchemaGenerator.shapeSchemaAccessor(null, shape));

    assertTrue(
        ex.getMessage().contains("Unsupported Smithy prelude schema shape smithy.api#Unsupported"),
        ex.getMessage());
    assertTrue(
        ex.getMessage().contains("Supported prelude schema shapes: Boolean, Byte"),
        ex.getMessage());
    assertTrue(ex.getMessage().contains("Document, Unit."), ex.getMessage());
  }
}

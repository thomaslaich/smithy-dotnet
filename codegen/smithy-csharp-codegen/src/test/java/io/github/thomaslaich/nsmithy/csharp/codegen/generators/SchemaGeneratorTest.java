package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.codegen.core.SchemaSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;

final class SchemaGeneratorTest {

  @Test
  void unsupportedPreludeSchemaDiagnosticNamesSupportedShapes() {
    var shape = StructureShape.builder().id(ShapeId.from("smithy.api#Unsupported")).build();

    CodegenException ex =
        assertThrows(
            CodegenException.class, () -> SchemaGenerator.shapeSchemaAccessor(null, null, shape));

    assertTrue(
        ex.getMessage().contains("Unsupported Smithy prelude schema shape smithy.api#Unsupported"),
        ex.getMessage());
    assertTrue(
        ex.getMessage().contains("Supported prelude schema shapes: Boolean, Byte"),
        ex.getMessage());
    assertTrue(ex.getMessage().contains("Document, Unit."), ex.getMessage());
  }

  @Test
  void primitivePreludeShapesShareTheBoxedSchemas() {
    var prelude = Model.assembler().assemble().unwrap();
    var writer = new CSharpWriter("example");
    for (String name :
        new String[] {"Boolean", "Byte", "Short", "Integer", "Long", "Float", "Double"}) {
      var boxed = prelude.expectShape(ShapeId.from("smithy.api#" + name));
      var primitive = prelude.expectShape(ShapeId.from("smithy.api#Primitive" + name));

      assertEquals(
          SchemaGenerator.shapeSchemaAccessor(writer, null, boxed),
          SchemaGenerator.shapeSchemaAccessor(writer, null, primitive));
    }
  }

  @Test
  void runtimeSchemasOmitBuildTimeTraits() {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "model.smithy",
                """
                $version: "2"
                namespace example.weather

                @trait
                string owner

                /// Returns a forecast.
                @owner("weather-team")
                @readonly
                @examples([{ title: "Zurich" }])
                @tags(["public"])
                @externalDocumentation(Guide: "https://example.com")
                @deprecated
                operation GetForecast {}
                """)
            .assemble()
            .unwrap();
    var operation = model.expectShape(ShapeId.from("example.weather#GetForecast"));

    var retained =
        operation.getAllTraits().values().stream()
            .filter(SchemaSupport::isRuntimeTrait)
            .map(trait -> trait.toShapeId().toString())
            .sorted()
            .toList();

    assertEquals(List.of("example.weather#owner", "smithy.api#readonly"), retained);
  }

  @Test
  void typeNamedSchemasFailsWithAClearError() {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "model.smithy",
                """
                $version: "2"
                namespace example.weather
                service Weather { version: "1", operations: [GetSchemas] }
                operation GetSchemas { input := { schemas: Schemas } }
                structure Schemas { name: String }
                """)
            .assemble()
            .unwrap();
    var settings =
        io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings.fromNode(
            software.amazon.smithy.model.node.ObjectNode.builder()
                .withMember(
                    "service",
                    software.amazon.smithy.model.node.Node.from("example.weather#Weather"))
                .build());
    var symbols =
        new io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider(model, settings);

    CodegenException ex =
        assertThrows(
            CodegenException.class,
            () -> symbols.toSymbol(model.expectShape(ShapeId.from("example.weather#Schemas"))));

    assertTrue(ex.getMessage().contains("C# type named Schemas"), ex.getMessage());
    assertTrue(ex.getMessage().contains("Example.Weather.Schemas namespace"), ex.getMessage());
  }
}

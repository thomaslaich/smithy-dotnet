package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Path;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;

final class TypeNameGeneratorTest {
  @TempDir Path tempDir;

  private static final String MODEL =
      """
      $version: "2"
      namespace example.library

      /// Example.Library.DeleteBookInput stays qualified in documentation.
      structure DeleteBookInput {
          item: Book
          other: example.other#Book
          helper: Builder
          serializer: GeneratedSchema
      }
      structure Book {}
      structure Builder {}
      structure GeneratedSchema {}
      structure T {}
      union Choice {
          book: Book
          generic: T
      }
      """;

  @Test
  void shortensLocalTypesAndSchemaAccessorsWithoutChangingDocumentation() {
    var context = context();
    var factory = new CSharpWriter.CSharpWriterFactory(context.model(), context.settings());
    var shape =
        context
            .model()
            .expectShape(ShapeId.from("example.library#DeleteBookInput"), StructureShape.class);
    var recordWriter = factory.apply("test.g.cs", "Example.Library");
    new StructureGenerator(context, recordWriter, shape).run();
    var schemaWriter = factory.apply("test.Schema.g.cs", "Example.Library.Schemas");
    SchemaGenerator.writeStructureSchema(
        schemaWriter, context, shape, List.copyOf(shape.members()));
    String record = recordWriter.toString();
    String schema = schemaWriter.toString();

    assertTrue(record.contains("Book? Item"), record);
    assertTrue(record.contains("global::Example.Other.Book? Other"), record);
    assertTrue(record.contains("global::Example.Library.Builder? Helper"), record);
    assertTrue(record.contains("global::Example.Library.GeneratedSchema? Serializer"), record);
    assertFalse(record.contains("global::Example.Library.Book?"), record);
    assertTrue(
        record.contains("Example.Library.DeleteBookInput stays qualified in documentation."),
        record);

    assertTrue(schema.contains("namespace Example.Library.Schemas;"), schema);
    assertTrue(
        schema.contains("StructSchema<global::Example.Library.DeleteBookInput, Builder> Schema"),
        schema);
    assertTrue(schema.contains("serializer.WriteStruct(0, value0, BookSchema.Schema);"), schema);
    assertTrue(schema.contains("BookSchema.Schema!"), schema);
    assertTrue(schema.contains("global::Example.Other.Schemas.BookSchema.Schema!"), schema);
    assertTrue(schema.contains("global::NSmithy.Core.Serde.Schemas.Lazy("), schema);
    assertTrue(schema.contains("private sealed class GeneratedSchema()"), schema);
    assertFalse(schema.contains("smithy.api#documentation"), schema);
    assertFalse(schema.contains("using Example.Other;"), schema);
  }

  @Test
  void qualifiesTypesHiddenByUnionVariantsAndTypeParameters() {
    var context = context();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Library");
    var shape =
        context.model().expectShape(ShapeId.from("example.library#Choice"), UnionShape.class);
    new UnionGenerator(context, writer, shape).run();
    SchemaGenerator.writeUnionSchema(writer, context, shape, ShapeSupport.sortedMembers(shape));
    String generated = writer.toString();
    assertTrue(generated.contains("public Book(global::Example.Library.Book value)"), generated);
    assertTrue(generated.contains("Func<global::Example.Library.T, T>"), generated);
    assertTrue(generated.contains("Schema<Choice> Schema"), generated);
    assertTrue(generated.contains("case Choice.Book @case:"), generated);
  }

  private GenerationContext context() {
    var model =
        Model.assembler()
            .addUnparsedModel("local.smithy", MODEL)
            .addUnparsedModel(
                "other.smithy",
                """
                $version: "2"
                namespace example.other
                structure Book {}
                """)
            .assemble()
            .unwrap();
    var settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", "example.library#Library")
                .withMember("baseNamespace", "")
                .build());
    var symbols = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(tempDir);
    return GenerationContext.builder()
        .model(model)
        .settings(settings)
        .symbolProvider(symbols)
        .fileManifest(manifest)
        .writerDelegator(new CSharpDelegator(manifest, symbols, model, settings))
        .build();
  }
}

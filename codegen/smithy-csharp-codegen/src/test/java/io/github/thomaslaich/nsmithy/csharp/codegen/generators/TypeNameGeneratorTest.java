package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Path;
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
          serializer: ValueSerializer
      }
      structure Book {}
      structure Builder {}
      structure ValueSerializer {}
      structure T {}
      union Choice {
          book: Book
          generic: T
      }
      """;

  @Test
  void shortensLocalTypesAndSchemaAccessorsWithoutChangingDocumentation() {
    var context = context();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Library");
    new StructureGenerator(
            context,
            writer,
            context
                .model()
                .expectShape(ShapeId.from("example.library#DeleteBookInput"), StructureShape.class))
        .run();
    String generated = writer.toString();
    assertTrue(generated.contains("Schema<DeleteBookInput> Schema"), generated);
    assertTrue(generated.contains("Book? Item"), generated);
    assertTrue(generated.contains("BookSchema.Schema!"), generated);
    assertTrue(generated.contains("global::Example.Other.Book? Other"), generated);
    assertTrue(generated.contains("global::Example.Other.BookSchema.Schema!"), generated);
    assertTrue(generated.contains("global::Example.Library.Builder? Helper"), generated);
    assertTrue(
        generated.contains("global::Example.Library.ValueSerializer? Serializer"), generated);
    assertTrue(
        generated.contains("/// Example.Library.DeleteBookInput stays qualified in documentation."),
        generated);
    assertFalse(generated.contains("global::Example.Library.Book"), generated);
    assertFalse(generated.contains("Example.Library.DeleteBookInput>"), generated);
    assertFalse(generated.contains("using Example.Other;"), generated);
  }

  @Test
  void qualifiesTypesHiddenByUnionVariantsAndTypeParameters() {
    var context = context();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Library");
    new UnionGenerator(
            context,
            writer,
            context.model().expectShape(ShapeId.from("example.library#Choice"), UnionShape.class))
        .run();
    String generated = writer.toString();
    assertTrue(generated.contains("public Book(global::Example.Library.Book value)"), generated);
    assertTrue(generated.contains("Func<global::Example.Library.T, T>"), generated);
    assertTrue(generated.contains("Schema<Choice> Schema"), generated);
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

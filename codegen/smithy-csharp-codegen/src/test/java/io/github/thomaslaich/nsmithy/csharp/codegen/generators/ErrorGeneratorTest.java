package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.nio.file.Files;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;

final class ErrorGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.weather

      service Weather {
          version: "1"
          operations: [GetForecast]
      }

      operation GetForecast {
          input := {}
          output := {}
          errors: [ThrottlingError, TransientError, ValidationError]
      }

      @error("client")
      @retryable(throttling: true)
      structure ThrottlingError {
          message: String
      }

      @error("server")
      @retryable
      structure TransientError {
          message: String
      }

      @error("client")
      structure ValidationError {
          message: String
      }
      """;

  @Test
  void retryableErrorsImplementRetryableErrorInterface() throws Exception {
    String throttling = renderError("example.weather#ThrottlingError");
    String transientError = renderError("example.weather#TransientError");

    assertTrue(
        throttling.contains(
            "public sealed partial class ThrottlingError : Exception,"
                + " NSmithy.Core.ISmithyRetryableError"),
        throttling);
    assertTrue(
        throttling.contains("bool NSmithy.Core.ISmithyRetryableError.IsThrottlingError => true;"),
        throttling);
    assertTrue(
        transientError.contains(
            "bool NSmithy.Core.ISmithyRetryableError.IsThrottlingError => false;"),
        transientError);
  }

  @Test
  void nonRetryableErrorsDoNotImplementRetryableErrorInterface() throws Exception {
    String generated = renderError("example.weather#ValidationError");

    assertTrue(
        generated.contains("public sealed partial class ValidationError : Exception"), generated);
    assertFalse(generated.contains("ISmithyRetryableError"), generated);
  }

  @Test
  void structuresGenerateDirectMemberSerialization() throws Exception {
    String generated = renderError("example.weather#ValidationError");

    assertTrue(
        generated.contains(
            "private sealed class ValueSerializer :"
                + " IStructValueSerializer<global::Example.Example.Weather.ValidationError>"),
        generated);
    assertTrue(generated.contains("writer.WriteMember<string?>(0, value.Message);"), generated);
    assertTrue(generated.contains("new ValueSerializer())"), generated);
  }

  private String renderError(String shapeId) throws Exception {
    Model model = Model.assembler().addUnparsedModel("model.smithy", MODEL).assemble().unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.weather#Weather"))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest =
        FileManifest.create(Files.createDirectory(tempDir.resolve(shapeId.replace('#', '-'))));
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbolProvider)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider, model, settings))
            .build();
    var writer =
        new CSharpWriter.CSharpWriterFactory(context.model(), context.settings())
            .apply("test.g.cs", "Example.Weather");
    var shape = model.expectShape(ShapeId.from(shapeId), StructureShape.class);

    new ErrorGenerator(context, writer, shape).run();

    return writer.toString();
  }
}

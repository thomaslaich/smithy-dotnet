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
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class FakeClientGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.fake

      use smithy.api#streaming

      service Fakeable {
          version: "1"
          operations: [GetCity, ListCities, Ping, Watch]
      }

      @examples([
          {
              title: "Get city",
              input: { id: "c1" },
              output: { name: "Zurich", population: 400000 }
          },
          {
              title: "Get missing city",
              input: { id: "nope" },
              error: {
                  shapeId: "example.fake#NoSuchCity",
                  content: { message: "unknown city" }
              }
          }
      ])
      operation GetCity {
          input := {
              @required
              id: String
          }
          output := {
              @required
              name: String
              population: Integer
          }
          errors: [NoSuchCity]
      }

      @error("client")
      structure NoSuchCity {
          @required
          message: String
      }

      @paginated(inputToken: "nextToken", outputToken: "nextToken", items: "items")
      operation ListCities {
          input := {
              nextToken: String
          }
          output := {
              nextToken: String
              items: CityList
          }
      }

      operation Ping {}

      operation Watch {
          output := {
              events: ChatEvent
          }
      }

      list CityList {
          member: CitySummary
      }

      structure CitySummary {
          name: String
      }

      @streaming
      union ChatEvent {
          message: MessageEvent
      }

      structure MessageEvent {
          text: String
      }
      """;

  @Test
  void emitsOverridableFakeClientClass() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("public class FakeFakeableClient : IFakeableClient"), generated);
    assertTrue(
        generated.contains(
            "public virtual"
                + " Task<GetCityOutput>"
                + " GetCityAsync(GetCityInput input,"
                + " CancellationToken cancellationToken = default)"),
        generated);
    assertTrue(generated.contains("public virtual void Dispose() { }"), generated);
  }

  @Test
  void usesExampleOutputWhenPresent() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "return Task.FromResult(new"
                + " GetCityOutput(Name: \"Zurich\", Population:"
                + " 400000));"),
        generated);
  }

  @Test
  void matchesExampleInputsAndThrowsMatchedErrorExamples() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("if (MatchesGetCityExample0(input))"), generated);
    assertTrue(
        generated.contains("private static bool MatchesGetCityExample1(GetCityInput" + " input)"),
        generated);
    assertTrue(generated.contains("if (!(input.Id == \"nope\"))"), generated);
    assertTrue(
        generated.contains(
            "return"
                + " Task.FromException<GetCityOutput>(new"
                + " NoSuchCity(message: \"unknown city\"));"),
        generated);
  }

  @Test
  void paginatorsYieldASinglePage() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "public virtual async"
                + " IAsyncEnumerable<ListCitiesOutput>"
                + " ListCitiesPagesAsync(ListCitiesInput input,"
                + " [EnumeratorCancellation]"
                + " CancellationToken cancellationToken = default)"),
        generated);
    assertTrue(
        generated.contains(
            "yield return await ListCitiesAsync(input, cancellationToken).ConfigureAwait(false);"),
        generated);
    // Unpaginated operations get no paginators.
    assertFalse(generated.contains("GetCityPagesAsync"), generated);
  }

  @Test
  void itemsPaginatorFlattensThePage() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "public virtual async"
                + " IAsyncEnumerable<CitySummary>"
                + " ListCitiesItemsAsync(ListCitiesInput input,"
                + " [EnumeratorCancellation]"
                + " CancellationToken cancellationToken = default)"),
        generated);
    assertTrue(
        generated.contains(
            "await foreach (var page in ListCitiesPagesAsync(input,"
                + " cancellationToken).ConfigureAwait(false))"),
        generated);
    assertTrue(generated.contains("var items = page.Items;"), generated);
    assertTrue(generated.contains("foreach (var item in items.Values)"), generated);
  }

  @Test
  void unitInputAndOutputCollapseToBareTask() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "public virtual Task" + " PingAsync(CancellationToken cancellationToken = default)"),
        generated);
    assertTrue(generated.contains("return Task.CompletedTask;"), generated);
  }

  @Test
  void eventStreamOutputsYieldFromGeneratedAsyncIterator() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("new WatchOutput(Events: FakeWatchEventsEvents())"), generated);
    assertTrue(
        generated.contains(
            "private static async" + " IAsyncEnumerable<ChatEvent>" + " FakeWatchEventsEvents()"),
        generated);
  }

  private String renderFake() throws Exception {
    Model model = Model.assembler().addUnparsedModel("model.smithy", MODEL).assemble().unwrap();
    CSharpSettings settings =
        CSharpSettings.fromNode(
            ObjectNode.builder()
                .withMember("service", Node.from("example.fake#Fakeable"))
                .withMember("baseNamespace", Node.from("Example"))
                .build());
    var symbolProvider = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(Files.createDirectory(tempDir.resolve("manifest")));
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbolProvider)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbolProvider))
            .build();
    var writer = new CSharpWriter("Example.Example.Fake");
    var service = model.expectShape(ShapeId.from("example.fake#Fakeable"), ServiceShape.class);

    new FakeClientGenerator(context, writer, service).run();

    return writer.toString();
  }
}

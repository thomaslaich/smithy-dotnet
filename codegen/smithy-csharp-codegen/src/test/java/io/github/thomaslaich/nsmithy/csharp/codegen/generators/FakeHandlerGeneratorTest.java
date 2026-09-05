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

final class FakeHandlerGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.fake

      use smithy.api#streaming

      service Fakeable {
          version: "1"
          operations: [GetCity, GetStatus, GetTree, Lookup, Ping, Watch]
      }

      @examples([
          {
              title: "Get city",
              input: { id: "c1" },
              output: { name: "Zurich", population: 400000, tags: ["alpine"] }
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
              tags: TagList
              mood: Mood
          }
      }

      operation GetStatus {
          output := {
              @required
              label: String
              @length(min: 6)
              code: String
              @range(min: 5)
              count: Integer
              when: Timestamp
              mood: Mood
              choice: Choice
              attrs: AttrMap
          }
      }

      operation GetTree {
          output := {
              @required
              root: TreeNode
          }
      }

      @examples([
          {
              title: "Lookup Zurich",
              input: { query: "zrh", limit: 1 },
              output: { label: "Zurich" }
          },
          {
              title: "Lookup by filter",
              input: { filter: { tags: ["a", "b"] } },
              output: { label: "Filtered" }
          },
          {
              title: "Lookup missing",
              input: { query: "nope" },
              error: {
                  shapeId: "example.fake#LookupError",
                  content: { message: "no such city", hint: "try zrh" }
              }
          }
      ])
      operation Lookup {
          input := {
              query: String
              limit: Integer
              filter: Filter
          }
          output := {
              @required
              label: String
          }
          errors: [LookupError]
      }

      structure Filter {
          tags: TagList
      }

      @error("client")
      structure LookupError {
          message: String
          hint: String
      }

      operation Ping {}

      operation Watch {
          output := {
              events: ChatEvent
          }
      }

      list TagList {
          member: String
      }

      map AttrMap {
          key: String
          value: String
      }

      enum Mood {
          HAPPY
          SAD
      }

      union Choice {
          num: Integer
          word: String
      }

      structure TreeNode {
          @required
          id: String
          child: TreeNode
          children: TreeList
      }

      list TreeList {
          member: TreeNode
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
  void emitsOverridableFakeHandlerClass() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains("public class FakeFakeableServiceHandler : IFakeableServiceHandler"),
        generated);
    assertTrue(
        generated.contains(
            "public virtual"
                + " System.Threading.Tasks.Task<GetCityOutput>"
                + " GetCityAsync(GetCityInput input,"
                + " System.Threading.CancellationToken cancellationToken = default)"),
        generated);
    assertFalse(generated.contains("AddFakeFakeableServiceHandler"), generated);
  }

  @Test
  void usesExampleOutputWhenPresentAndOmitsAbsentOptionalMembers() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "return System.Threading.Tasks.Task.FromResult(new"
                + " GetCityOutput(Name: \"Zurich\", Population:"
                + " 400000, Tags: new TagList(new string[] {"
                + " \"alpine\" })));"),
        generated);
    assertFalse(generated.contains("GetCityOutput(Name: \"Zurich\", Mood:"), generated);
  }

  @Test
  void synthesizesConstraintAwarePlaceholdersWithoutExamples() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "new GetStatusOutput(Label: \"label\","
                + " Attrs: new AttrMap(new"
                + " System.Collections.Generic.Dictionary<string, string> { { \"key\", \"value\" }"
                + " }), Choice: Choice.FromNum(0), Code: \"codexx\","
                + " Count: 5, Mood: Mood.HAPPY,"
                + " When: System.DateTimeOffset.FromUnixTimeSeconds(1704067200))"),
        generated);
  }

  @Test
  void breaksRecursionThroughOptionalMembersAndCollections() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "new GetTreeOutput(Root: new"
                + " TreeNode(Id: \"id\", Children: new"
                + " TreeList("
                + "System.Array.Empty<TreeNode>())))"),
        generated);
    assertFalse(generated.contains("Child: new TreeNode"), generated);
  }

  @Test
  void unitInputAndOutputCollapseToBareTask() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "public virtual System.Threading.Tasks.Task"
                + " PingAsync(System.Threading.CancellationToken cancellationToken = default)"),
        generated);
    assertTrue(generated.contains("return System.Threading.Tasks.Task.CompletedTask;"), generated);
  }

  @Test
  void eventStreamOutputsYieldFromGeneratedAsyncIterator() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("new WatchOutput(Events: FakeWatchEventsEvents())"), generated);
    assertTrue(
        generated.contains(
            "private static async"
                + " System.Collections.Generic.IAsyncEnumerable<ChatEvent>"
                + " FakeWatchEventsEvents()"),
        generated);
    assertTrue(
        generated.contains(
            "yield return ChatEvent.FromMessage(new" + " MessageEvent(Text: \"text\"));"),
        generated);
  }

  @Test
  void multipleExamplesMatchInputInModelOrderWithFallback() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("if (MatchesLookupExample0(input))"), generated);
    assertTrue(generated.contains("if (MatchesLookupExample1(input))"), generated);
    assertTrue(generated.contains("if (MatchesLookupExample2(input))"), generated);
    assertTrue(
        generated.contains("private static bool MatchesLookupExample0(LookupInput" + " input)"),
        generated);
    assertTrue(generated.contains("if (!(input.Query == \"zrh\"))"), generated);
    assertTrue(generated.contains("if (!(input.Limit == 1))"), generated);
    assertTrue(
        generated.contains(
            "return System.Threading.Tasks.Task.FromResult(new"
                + " LookupOutput(Label: \"Filtered\"));"),
        generated);
    // The fallback stays the first non-error example output.
    assertTrue(
        generated.contains(
            "return System.Threading.Tasks.Task.FromResult(new"
                + " LookupOutput(Label: \"Zurich\"));"),
        generated);
  }

  @Test
  void nestedStructureAndListExampleInputsCompareStructurally() throws Exception {
    String generated = renderFake();

    assertTrue(generated.contains("if (!(input.Filter is { } v0))"), generated);
    assertTrue(generated.contains("if (!(v0.Tags is { } v1))"), generated);
    assertTrue(generated.contains("if (!(v1.Values.Count == 2))"), generated);
    assertTrue(generated.contains("if (!(v1.Values[0] == \"a\"))"), generated);
    assertTrue(generated.contains("if (!(v1.Values[1] == \"b\"))"), generated);
  }

  @Test
  void matchedErrorExampleThrowsTheModeledError() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "return"
                + " System.Threading.Tasks.Task.FromException<LookupOutput>(new"
                + " LookupError(message: \"no such city\", hint: \"try"
                + " zrh\"));"),
        generated);
  }

  @Test
  void singleNonErrorExampleGeneratesNoMatching() throws Exception {
    String generated = renderFake();

    assertFalse(generated.contains("MatchesGetCityExample"), generated);
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

    new FakeHandlerGenerator(context, writer, service).run();

    return writer.toString();
  }
}

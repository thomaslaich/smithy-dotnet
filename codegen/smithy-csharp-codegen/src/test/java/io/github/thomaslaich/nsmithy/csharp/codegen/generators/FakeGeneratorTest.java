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

final class FakeGeneratorTest {

  @TempDir java.nio.file.Path tempDir;

  private static final String MODEL =
      """
      $version: "2"

      namespace example.fake

      use smithy.api#streaming

      service Fakeable {
          version: "1"
          operations: [GetCity, GetStatus, GetTree, Ping, Watch]
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
                + " System.Threading.Tasks.Task<Example.Example.Fake.GetCityOutput>"
                + " GetCityAsync(Example.Example.Fake.GetCityInput input,"
                + " System.Threading.CancellationToken cancellationToken = default)"),
        generated);
    assertFalse(generated.contains("AddFakeFakeableServiceHandler"), generated);
  }

  @Test
  void usesExampleOutputWhenPresentAndOmitsAbsentOptionalMembers() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "return System.Threading.Tasks.Task.FromResult(new Example.Example.Fake.GetCityOutput("
                + "Name: \"Zurich\", Population: 400000, Tags: new Example.Example.Fake.TagList("
                + "new string[] { \"alpine\" })));"),
        generated);
    assertFalse(generated.contains("GetCityOutput(Name: \"Zurich\", Mood:"), generated);
  }

  @Test
  void synthesizesConstraintAwarePlaceholdersWithoutExamples() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "new Example.Example.Fake.GetStatusOutput(Label: \"label\","
                + " Attrs: new Example.Example.Fake.AttrMap(new"
                + " System.Collections.Generic.Dictionary<string, string> { { \"key\", \"value\" }"
                + " }), Choice: Example.Example.Fake.Choice.FromNum(0), Code: \"codexx\","
                + " Count: 5, Mood: Example.Example.Fake.Mood.HAPPY,"
                + " When: System.DateTimeOffset.FromUnixTimeSeconds(1704067200))"),
        generated);
  }

  @Test
  void breaksRecursionThroughOptionalMembersAndCollections() throws Exception {
    String generated = renderFake();

    assertTrue(
        generated.contains(
            "new Example.Example.Fake.GetTreeOutput(Root: new Example.Example.Fake.TreeNode("
                + "Id: \"id\", Children: new Example.Example.Fake.TreeList("
                + "System.Array.Empty<Example.Example.Fake.TreeNode>())))"),
        generated);
    assertFalse(generated.contains("Child: new Example.Example.Fake.TreeNode"), generated);
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

    assertTrue(
        generated.contains("new Example.Example.Fake.WatchOutput(Events: FakeWatchEventsEvents())"),
        generated);
    assertTrue(
        generated.contains(
            "private static async"
                + " System.Collections.Generic.IAsyncEnumerable<Example.Example.Fake.ChatEvent>"
                + " FakeWatchEventsEvents()"),
        generated);
    assertTrue(
        generated.contains(
            "yield return Example.Example.Fake.ChatEvent.FromMessage(new"
                + " Example.Example.Fake.MessageEvent(Text: \"text\"));"),
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

    new FakeGenerator(context, writer, service).run();

    return writer.toString();
  }
}

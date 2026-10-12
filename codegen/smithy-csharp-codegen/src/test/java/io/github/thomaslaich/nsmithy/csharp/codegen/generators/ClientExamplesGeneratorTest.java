package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.io.StringReader;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;
import javax.xml.parsers.DocumentBuilderFactory;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import org.xml.sax.InputSource;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;

final class ClientExamplesGeneratorTest {
  @TempDir Path tempDir;

  private static final String MODEL =
      """
      $version: "2"
      namespace example.docs
      service Examples {
          version: "1"
          operations: [Send, Ping, NoExamples, Upload, Watch]
      }
      /// Sends an example.
      @examples([
          {
              title: "Success <&> $L",
              documentation: "First line.\nSecond <line> & more.",
              input: {
                  text: "<&> $L \\\"quoted\\\"\\nnext",
                  nested: { text: "outer", child: { text: "inner" } },
                  choice: { text: "chosen" },
                  values: ["first", "second"],
                  tags: { z: "last", a: "start" },
                  state: "ready",
                  level: 1,
                  blob: "aGk=",
                  time: 1.25,
                  enabled: true,
                  amount: 12.5,
                  count: 42,
                  data: { special: "<&>", items: [1, null, true] }
              },
              output: { text: "done" }
          },
          {
              title: "Error",
              input: { text: "bad" },
              error: { shapeId: "example.docs#Failure", content: { message: "bad <&>", reason: "invalid" } }
          },
          { title: "Input only", input: { text: "only" } }
      ])
      operation Send {
          input: Request
          output: Response
          errors: [Failure]
      }
      structure Request {
          @required
          text: String
          nested: Nested
          choice: Choice
          values: StringValues
          tags: Tags
          state: State
          level: Level
          blob: Blob
          time: Timestamp
          enabled: Boolean
          amount: Double
          count: Long
          data: Document
      }
      structure Response { text: String }
      structure Nested { text: String, child: Nested }
      union Choice { text: String, empty: Unit }
      @sparse
      list StringValues { member: String }
      @sparse
      map Tags { key: String, value: String }
      enum State {
          READY = "ready"
      }
      intEnum Level {
          LOW = 1
      }
      @error("client")
      structure Failure { message: String, reason: String }
      @examples([{ title: "Ping" }])
      operation Ping {}
      operation NoExamples {}
      @examples([{ title: "Upload", input: { body: "aGk=" } }])
      operation Upload { input := { @required @httpPayload body: Body } }
      @streaming
      blob Body
      @examples([{ title: "Watch", output: { events: { message: { text: "hello" } } } }])
      operation Watch { output := { @httpPayload events: Events } }
      @streaming
      union Events { message: Response }
      """;

  @Test
  void rendersTypedValuesAndEscapesXmlWithoutLosingExampleText() throws Exception {
    String generated = render(MODEL);
    var examples = examples(generated);
    assertEquals(6, examples.size());
    String code =
        examples.stream()
            .filter(s -> s.contains("expectedOutput = new Response"))
            .findFirst()
            .orElseThrow();
    assertTrue(generated.contains("Success &lt;&amp;&gt; $L"), generated);
    assertTrue(generated.contains("Second &lt;line&gt; &amp; more."), generated);
    assertTrue(generated.contains("/// Sends an example."), generated);
    assertTrue(code.contains("Text: \"<&> $L \\\"quoted\\\"\\nnext\""), code);
    assertTrue(
        code.contains("new Nested(Child: new Nested(Text: \"inner\"), Text: \"outer\")"), code);
    assertTrue(code.contains("Choice.FromText(\"chosen\")"), code);
    assertTrue(code.contains("new string?[] { \"first\", \"second\" }"), code);
    assertTrue(
        code.contains(
            "new Dictionary<string, string?> { { \"a\", \"start\" }, { \"z\", \"last\" }" + " }"),
        code);
    assertTrue(code.contains("new State(\"ready\")"), code);
    assertTrue(code.contains("(Level)1"), code);
    assertTrue(code.contains("Convert.FromBase64String(\"aGk=\")"), code);
    assertTrue(code.contains("DateTimeOffset.UnixEpoch.AddTicks(12500000L)"), code);
    assertTrue(code.contains("Enabled: true"), code);
    assertTrue(code.contains("Amount: 12.5d"), code);
    assertTrue(code.contains("Count: 42L"), code);
    assertTrue(code.contains("Document.From(\"<&>\")"), code);
    assertTrue(code.contains("var output = await client.SendAsync(input);"), code);
    assertTrue(code.contains("var expectedOutput = new Response(Text: \"done\");"), code);
    assertFalse(code.contains("catch ("), code);
    assertEquals(generated, render(MODEL));
  }

  @Test
  void rendersErrorsUnitOperationsAndPartialExamples() throws Exception {
    var examples = examples(render(MODEL));
    String error =
        examples.stream().filter(s -> s.contains("catch (Failure)")).findFirst().orElseThrow();
    assertEquals(
        """
        var input = new Request(Text: "bad");
        try
        {
            await client.SendAsync(input);
        }
        catch (Failure)
        {
            // Expected Message: "bad <&>"
            // Expected Reason: "invalid"
        }
        """
            .strip(),
        error);
    assertFalse(error.contains("var output"), error);
    assertFalse(error.contains("expectedError"), error);
    assertFalse(error.contains("expectedOutput"), error);
    String partial =
        examples.stream().filter(s -> s.contains("Text: \"only\"")).findFirst().orElseThrow();
    assertFalse(partial.contains("expectedOutput"), partial);
    assertFalse(partial.contains("Nested:"), partial);
    assertTrue(examples.contains("await client.PingAsync();"));
    assertFalse(examples.stream().anyMatch(s -> s.contains("NoExamplesAsync")));
  }

  @Test
  void errorExamplesWithoutInputOutputOrContentUseATypedCatch() throws Exception {
    String model =
        MODEL.replace(
            "@examples([{ title: \"Ping\" }])\noperation Ping {}",
            """
            @examples([{ title: "Ping error", error: { shapeId: "example.docs#Failure", content: {} } }])
            operation Ping { errors: [Failure] }
            """
                .strip());
    String code =
        examples(render(model)).stream()
            .filter(s -> s.contains("client.PingAsync"))
            .findFirst()
            .orElseThrow();
    assertEquals(
        """
        try
        {
            await client.PingAsync();
        }
        catch (Failure)
        {
            // Expected exception; no example properties supplied.
        }
        """
            .strip(),
        code);
  }

  @Test
  void errorPropertiesUseGeneratedNamesAndPreserveNestedValues() throws Exception {
    String model =
        MODEL
            .replace(
                "message: String, reason: String",
                "MESSAGE: String, reason: String, detail: Nested")
            .replace(
                "message: \"bad <&>\", reason: \"invalid\"",
                "MESSAGE: \"bad <&>\", reason: \"invalid\", detail: { text: \"nested\" }");
    String code =
        examples(render(model)).stream()
            .filter(s -> s.contains("catch (Failure)"))
            .findFirst()
            .orElseThrow();
    assertTrue(code.contains("// Expected Message: \"bad <&>\""), code);
    assertTrue(code.contains("// Expected Detail: new Nested(Text: \"nested\")"), code);
    assertFalse(code.contains("Expected MESSAGE:"), code);
  }

  @Test
  void rendersStreamingValuesWithSelfContainedLocalIterators() throws Exception {
    var examples = examples(render(MODEL));
    String upload =
        examples.stream().filter(s -> s.contains("UploadAsync")).findFirst().orElseThrow();
    assertTrue(upload.contains("new MemoryStream(Convert.FromBase64String(\"aGk=\"))"), upload);
    String watch =
        examples.stream().filter(s -> s.contains("WatchAsync")).findFirst().orElseThrow();
    assertTrue(watch.contains("Events: ExampleEvents1()"), watch);
    assertTrue(watch.contains("static async IAsyncEnumerable<Events> ExampleEvents1()"), watch);
    assertTrue(
        watch.contains("\n    yield return Events.FromMessage(new Response(Text: \"hello\"));"),
        watch);
  }

  @Test
  void writesExamplesOnBothInterfaceAndConcreteMethods() throws Exception {
    String model = MODEL.replace("service Examples", "@aws.protocols#restJson1\nservice Examples");
    var examples = examples(render(model));
    assertEquals(12, examples.size());
    assertEquals(examples.subList(0, 6), examples.subList(6, 12));
  }

  @Test
  void preservesEmptyCollectionsUnitUnionsAndExplicitNullDocuments() throws Exception {
    String model =
        MODEL
            .replace("values: [\"first\", \"second\"]", "values: []")
            .replace("tags: { z: \"last\", a: \"start\" }", "tags: {}")
            .replace("choice: { text: \"chosen\" }", "choice: { empty: {} }")
            .replace("data: { special: \"<&>\", items: [1, null, true] }", "data: null");
    String code =
        examples(render(model)).stream()
            .filter(s -> s.contains("expectedOutput = new Response"))
            .findFirst()
            .orElseThrow();
    assertTrue(code.contains("new string?[] {  }"), code);
    assertTrue(code.contains("new Dictionary<string, string?> {  }"), code);
    assertTrue(code.contains("Choice.FromEmpty(SmithyUnit.Value)"), code);
    assertTrue(code.contains("Data: Document.Null"), code);
  }

  @Test
  void omittedInputUsesDeclaredDefaultsAndMarksMissingRequiredValues() throws Exception {
    String model =
        MODEL
            .replace("input: { text: \"bad\" }", "input: {}, allowConstraintErrors: true")
            .replace("count: Long", "@default(7) count: Long");
    String code =
        examples(render(model)).stream()
            .filter(s -> s.contains("omitted in example"))
            .findFirst()
            .orElseThrow();
    assertTrue(code.contains("Text: default! /* omitted in example */"), code);
    assertFalse(code.contains("Count:"), code);
  }

  private List<String> examples(String generated) throws Exception {
    var matcher = Pattern.compile("(?m)(?:^[ \\t]*///[^\\n]*\\n)+").matcher(generated);
    List<String> codes = new ArrayList<>();
    while (matcher.find()) {
      String xml = matcher.group().replaceAll("(?m)^[ \\t]*/// ?", "");
      var document =
          DocumentBuilderFactory.newInstance()
              .newDocumentBuilder()
              .parse(new InputSource(new StringReader("<docs>" + xml + "</docs>")));
      var blocks = document.getElementsByTagName("code");
      for (int i = 0; i < blocks.getLength(); i++) {
        codes.add(blocks.item(i).getTextContent().strip());
      }
    }
    return codes;
  }

  private String render(String source) {
    Model model =
        Model.assembler()
            .addUnparsedModel("model.smithy", source)
            .addUnparsedModel(
                "protocol.smithy",
                """
                $version: "2"
                namespace aws.protocols
                @trait(selector: "service")
                @protocolDefinition
                structure restJson1 {}
                """)
            .assemble()
            .unwrap();
    var settings =
        CSharpSettings.fromNode(
            Node.objectNode()
                .withMember("service", "example.docs#Examples")
                .withMember("baseNamespace", "Example"));
    var symbols = new CSharpSymbolProvider(model, settings);
    var manifest = FileManifest.create(tempDir);
    var context =
        GenerationContext.builder()
            .model(model)
            .settings(settings)
            .symbolProvider(symbols)
            .fileManifest(manifest)
            .writerDelegator(new CSharpDelegator(manifest, symbols, model, settings))
            .build();
    var writer =
        new CSharpWriter.CSharpWriterFactory(model, settings)
            .apply("test.g.cs", settings.csharpNamespace("example.docs"));
    new ClientGenerator(
            context,
            writer,
            model.expectShape(ShapeId.from("example.docs#Examples"), ServiceShape.class))
        .run();
    return writer.toString();
  }
}

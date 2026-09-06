package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ObjectNode;

final class CSharpWriterTest {
  @Test
  void importsFrameworkTypesWithoutRewritingLiteralText() {
    var writer = new CSharpWriter("Example.Library");
    writer.write("// System.Threading.CancellationToken remains in comments.");
    writer.write(
        "public const string Name = $L;",
        CSharpNaming.formatString("System.Threading.CancellationToken"));
    writer.write(
        "public $L<int> Read($L cancellationToken);",
        writer.frameworkType("System.Threading.Tasks.Task"),
        writer.frameworkType("System.Threading.CancellationToken"));
    String generated = writer.toString();
    assertTrue(
        generated.contains("using System.Threading;\nusing System.Threading.Tasks;"), generated);
    assertTrue(
        generated.contains("Task<int> Read(CancellationToken cancellationToken)"), generated);
    assertTrue(
        generated.contains("// System.Threading.CancellationToken remains in comments."),
        generated);
    assertTrue(generated.contains("Name = \"System.Threading.CancellationToken\";"), generated);
    assertEquals(generated, writer.toString());
  }

  @Test
  void lateReservationsQualifyEarlierReferencesAndDoNotLeaveStaleImports() {
    var writer = new CSharpWriter("Example.Library");
    writer.write("public $L Read();", writer.frameworkType("System.Threading.Tasks.Task"));
    assertTrue(writer.toString().contains("using System.Threading.Tasks;"));
    writer.reserveName("Task");
    String generated = writer.toString();
    assertTrue(generated.contains("public global::System.Threading.Tasks.Task Read();"), generated);
    assertFalse(generated.contains("using System.Threading.Tasks;"), generated);
  }

  @Test
  void conflictingFrameworkImportsAreQualifiedRegardlessOfReferenceOrder() {
    var writer = new CSharpWriter("Example.Library");
    writer.write("public $L First;", writer.frameworkType("System.Threading.Timer"));
    writer.write("public $L Second;", writer.frameworkType("System.Timers.Timer"));
    String generated = writer.toString();
    assertTrue(generated.contains("global::System.Threading.Timer First"), generated);
    assertTrue(generated.contains("global::System.Timers.Timer Second"), generated);
    assertFalse(generated.contains("using System.Threading;"), generated);
    assertFalse(generated.contains("using System.Timers;"), generated);
  }

  @Test
  void attributesUseShortNamesUnlessEitherAttributeSpellingIsHidden() {
    for (String collision :
        new String[] {"EnumeratorCancellation", "EnumeratorCancellationAttribute"}) {
      var writer = new CSharpWriter("Example.Library");
      writer.write(
          "[$L]",
          writer.frameworkAttribute(
              "System.Runtime.CompilerServices.EnumeratorCancellationAttribute"));
      assertTrue(writer.toString().contains("[EnumeratorCancellation]"));
      writer.reserveName(collision);
      assertTrue(
          writer
              .toString()
              .contains(
                  "[global::System.Runtime.CompilerServices.EnumeratorCancellationAttribute]"));
    }
  }

  @Test
  void unreferencedDeclarationsInCurrentAndParentNamespacesAlsoPreventFrameworkImports() {
    var model =
        Model.assembler()
            .addUnparsedModel(
                "local.smithy",
                """
                $version: "2"
                namespace example.library
                structure CancellationToken {}
                service Http { version: "1" }
                """)
            .addUnparsedModel(
                "parent.smithy",
                """
                $version: "2"
                namespace example
                structure Task {}
                """)
            .assemble()
            .unwrap();
    var settings =
        CSharpSettings.fromNode(
            ObjectNode.builder().withMember("service", "example.library#Http").build());
    var writer =
        new CSharpWriter.CSharpWriterFactory(model, settings).apply("test.g.cs", "Example.Library");
    writer.write(
        "public $L Read($L token, $L client);",
        writer.frameworkType("System.Threading.Tasks.Task"),
        writer.frameworkType("System.Threading.CancellationToken"),
        writer.frameworkType("System.Net.Http.HttpClient"));
    String generated = writer.toString();
    assertTrue(generated.contains("global::System.Threading.Tasks.Task Read"), generated);
    assertTrue(generated.contains("global::System.Threading.CancellationToken token"), generated);
    assertTrue(generated.contains("global::System.Net.Http.HttpClient client"), generated);
  }
}

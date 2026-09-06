package io.github.thomaslaich.nsmithy.csharp.codegen.writer;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSettings;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import org.junit.jupiter.api.Test;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolReference;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ObjectNode;

final class CSharpWriterTest {
  @Test
  void runtimeSymbolsUseImportsAndQualifyLateCollisions() {
    var writer = new CSharpWriter("Example.Library");
    writer.write("public $T Read();", RuntimeTypes.DOCUMENT);
    assertTrue(writer.toString().contains("using NSmithy.Core;"));
    assertTrue(writer.toString().contains("public Document Read();"));
    writer.reserveName("Document");
    assertTrue(writer.toString().contains("public global::NSmithy.Core.Document Read();"));
    assertFalse(writer.toString().contains("using NSmithy.Core;"));
  }

  @Test
  void symbolFormatterRegistersImportsAndResolvesLateCollisions() {
    var writer = new CSharpWriter("Example.Library");
    var task = Symbol.builder().name("Task").namespace("System.Threading.Tasks", ".").build();
    writer.write("public $T Read();", task);
    assertTrue(writer.getImportContainer().toString().contains("using System.Threading.Tasks;"));
    writer.reserveName("Task");
    assertTrue(writer.toString().contains("public global::System.Threading.Tasks.Task Read();"));
    assertFalse(writer.getImportContainer().toString().contains("using System.Threading.Tasks;"));
  }

  @Test
  void symbolReferenceAliasesProduceAliasDirectives() {
    var writer = new CSharpWriter("Example.Library");
    var symbol = Symbol.builder().name("Timer").namespace("System.Threading", ".").build();
    var alias = SymbolReference.builder().symbol(symbol).alias("ThreadTimer").build();
    writer.write("public $T Timer;", alias);
    String generated = writer.toString();
    assertTrue(
        generated.contains("using ThreadTimer = global::System.Threading.Timer;"), generated);
    assertTrue(generated.contains("public ThreadTimer Timer;"), generated);
    writer.reserveName("ThreadTimer");
    generated = writer.toString();
    assertFalse(generated.contains("using ThreadTimer ="), generated);
    assertTrue(generated.contains("public global::System.Threading.Timer Timer;"), generated);
  }

  @Test
  void symbolImportsOmitCurrentNamespaceAndDeduplicateExplicitImports() {
    var imports = new ImportDeclarations("System.Threading");
    imports.importSymbol(
        Symbol.builder().name("CancellationToken").namespace("System.Threading", ".").build());
    assertEquals("", imports.toString());
    imports.importSymbol(
        Symbol.builder().name("Task").namespace("System.Threading.Tasks", ".").build());
    imports.importNamespace("System.Threading.Tasks");
    assertEquals("using System.Threading.Tasks;\n\n", imports.toString());
  }

  @Test
  void importsFrameworkTypesWithoutRewritingLiteralText() {
    var writer = new CSharpWriter("Example.Library");
    writer.write("// System.Threading.CancellationToken remains in comments.");
    writer.write(
        "public const string Name = $L;",
        CSharpNaming.formatString("System.Threading.CancellationToken"));
    writer.write(
        "public $L<int> Read($L cancellationToken);",
        writer.typeName(RuntimeTypes.TASK),
        writer.typeName(RuntimeTypes.CANCELLATION_TOKEN));
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
    writer.write("public $L Read();", writer.typeName(RuntimeTypes.TASK));
    assertTrue(writer.toString().contains("using System.Threading.Tasks;"));
    writer.reserveName("Task");
    String generated = writer.toString();
    assertTrue(generated.contains("public global::System.Threading.Tasks.Task Read();"), generated);
    assertFalse(generated.contains("using System.Threading.Tasks;"), generated);
  }

  @Test
  void conflictingFrameworkImportsAreQualifiedRegardlessOfReferenceOrder() {
    var writer = new CSharpWriter("Example.Library");
    writer.write(
        "public $L First;",
        writer.typeName(Symbol.builder().name("Timer").namespace("System.Threading", ".").build()));
    writer.write(
        "public $L Second;",
        writer.typeName(Symbol.builder().name("Timer").namespace("System.Timers", ".").build()));
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
      writer.write("[$L]", writer.attributeName(RuntimeTypes.ENUMERATOR_CANCELLATION_ATTRIBUTE));
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
        writer.typeName(RuntimeTypes.TASK),
        writer.typeName(RuntimeTypes.CANCELLATION_TOKEN),
        writer.typeName(RuntimeTypes.HTTP_CLIENT));
    String generated = writer.toString();
    assertTrue(generated.contains("global::System.Threading.Tasks.Task Read"), generated);
    assertTrue(generated.contains("global::System.Threading.CancellationToken token"), generated);
    assertTrue(generated.contains("global::System.Net.Http.HttpClient client"), generated);
  }

  @Test
  void unrelatedNamespaceSegmentsRemainReservedButUnrelatedTypeNamesDoNot() {
    var model =
        Model.assembler()
            .addUnparsedModel(
                "task.smithy",
                """
                $version: "2"
                namespace unrelated.task
                structure CancellationToken {}
                """)
            .addUnparsedModel(
                "attribute.smithy",
                """
                $version: "2"
                namespace unrelated.enumeratorCancellation
                structure Placeholder {}
                """)
            .assemble()
            .unwrap();
    var settings =
        CSharpSettings.fromNode(
            ObjectNode.builder().withMember("service", "example.library#Library").build());
    var writer =
        new CSharpWriter.CSharpWriterFactory(model, settings).apply("test.g.cs", "Example.Library");
    writer.write(
        "public $L Read([$L] $L token);",
        writer.typeName(RuntimeTypes.TASK),
        writer.attributeName(RuntimeTypes.ENUMERATOR_CANCELLATION_ATTRIBUTE),
        writer.typeName(RuntimeTypes.CANCELLATION_TOKEN));
    String generated = writer.toString();
    assertTrue(generated.contains("global::System.Threading.Tasks.Task Read("), generated);
    assertTrue(
        generated.contains(
            "[global::System.Runtime.CompilerServices.EnumeratorCancellationAttribute]"),
        generated);
    assertTrue(generated.contains("CancellationToken token"), generated);
    assertTrue(generated.contains("using System.Threading;"), generated);
    assertFalse(generated.contains("global::System.Threading.CancellationToken"), generated);
    assertFalse(generated.contains("using System.Threading.Tasks;"), generated);
    assertFalse(generated.contains("using System.Runtime.CompilerServices;"), generated);
  }
}

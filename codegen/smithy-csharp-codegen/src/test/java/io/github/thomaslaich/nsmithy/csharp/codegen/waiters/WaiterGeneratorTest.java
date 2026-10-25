package io.github.thomaslaich.nsmithy.csharp.codegen.waiters;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpCodegenPlugin;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class WaiterGeneratorTest {

  @TempDir Path tempDir;

  private static final String MODEL =
      """
      $version: "2"
      namespace example.jobs

      use smithy.waiters#waitable

      service Jobs {
          version: "1"
          operations: [GetJob]
      }

      @readonly
      @waitable(
          JobDone: {
              acceptors: [
                  {
                      state: "success"
                      matcher: { output: { path: "%s", expected: "%s", comparator: "%s" } }
                  }
                  {
                      state: "retry"
                      matcher: { errorType: "JobNotFound" }
                  }
                  {
                      state: "failure"
                      matcher: { errorType: "Job.Expired" }
                  }
              ]
          }
      )
      operation GetJob {
          input := { id: String }
          output := { status: String, steps: Steps }
          errors: [JobNotFound]
      }

      list Steps {
          member: String
      }

      @error("client")
      structure JobNotFound {}
      """;

  @Test
  void writesOneAcceptorTablePerWaiter() throws Exception {
    String waiters = generate(model("status", "DONE", "stringEquals"));

    assertTrue(waiters.contains("internal static class JobsWaiters"), waiters);
    assertTrue(waiters.contains("// output: status stringEquals \"DONE\""), waiters);
    assertTrue(waiters.contains("((output)?.Status) == \"DONE\""), waiters);
  }

  @Test
  void aModeledErrorMatchesByTypeAndAnyOtherByErrorCode() throws Exception {
    String waiters = generate(model("status", "DONE", "stringEquals"));

    assertTrue(waiters.contains("error is JobNotFound"), waiters);
    assertTrue(
        waiters.contains("error is SmithyClientException { ErrorCode: \"Job.Expired\" }"), waiters);
  }

  @Test
  void anUnsupportedPathFailsAndNamesTheWaiter() {
    var error =
        assertThrows(
            CodegenException.class, () -> generate(model("steps[0]", "DONE", "stringEquals")));

    assertTrue(error.getMessage().contains("Waiter JobDone"), error.getMessage());
    assertTrue(error.getMessage().contains("steps[0]"), error.getMessage());
  }

  @Test
  void aServiceWithoutWaitersGetsNoWaitersFile() throws Exception {
    Model model =
        Model.assembler()
            .addUnparsedModel(
                "plain.smithy",
                """
                $version: "2"
                namespace example.plain
                service Plain { version: "1", operations: [Ping] }
                operation Ping {}
                """)
            .assemble()
            .unwrap();
    run(model, "example.plain#Plain");

    assertFalse(Files.exists(tempDir.resolve("Example/Plain/Plain.Waiters.Client.g.cs")));
  }

  private static String model(String path, String expected, String comparator) {
    return MODEL.formatted(path, expected, comparator);
  }

  private String generate(String source) throws Exception {
    Model model =
        Model.assembler()
            .discoverModels(WaiterGeneratorTest.class.getClassLoader())
            .addUnparsedModel("jobs.smithy", source)
            .assemble()
            .unwrap();
    run(model, "example.jobs#Jobs");
    return Files.readString(tempDir.resolve("Example/Jobs/Jobs.Waiters.Client.g.cs"));
  }

  private void run(Model model, String service) {
    new CSharpCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(FileManifest.create(tempDir))
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from(service))
                        .withMember("baseNamespace", Node.from(""))
                        .build())
                .build());
  }
}

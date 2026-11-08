package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpCodegenPlugin;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.build.PluginContext;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;

class SmokeTestGeneratorTest {

  @TempDir Path tempDir;

  private static final String MODEL =
      """
      $version: "2"
      namespace example.jobs

      use smithy.test#smokeTests

      service Jobs {
          version: "1"
          operations: [GetJob, Ping]
          errors: [Throttled]
      }

      @readonly
      @smokeTests([
          {
              id: "GetJobSucceeds"
              params: { id: "job-1", limit: 2 }
              tags: ["read", "jobs"]
              expect: { success: {} }
          }
          {
              id: "GetJobIsMissing"
              params: { id: "missing" }
              expect: { failure: { errorId: JobNotFound } }
          }
          {
              id: "GetJobIsExpired"
              params: { id: "expired" }
              expect: { failure: { errorId: JobExpired } }
          }
          {
              id: "GetJobFails"
              params: { id: "" }
              expect: { failure: {} }
          }
      ])
      operation GetJob {
          input := {
              @required
              id: String
              limit: Integer
          }
          output := { status: String }
          errors: [JobNotFound]
      }

      @smokeTests([{ id: "PingSucceeds", expect: { success: {} } }])
      operation Ping {}

      @error("client")
      structure JobNotFound {}

      @error("client")
      structure JobExpired {}

      @error("client")
      structure Throttled {}
      """;

  @Test
  void writesOneTestPerCaseWithItsInputAndTags() throws Exception {
    String tests = generate(true);

    assertTrue(tests.contains("public static class JobsSmokeTests"), tests);
    assertTrue(
        tests.contains(
            "IReadOnlyList<SmithySmokeTest<JobsClientConfig, IJobsClient>> All { get; }"),
        tests);
    assertTrue(tests.contains("\"GetJobSucceeds\""), tests);
    assertTrue(tests.contains("[\"read\", \"jobs\"]"), tests);
    assertTrue(tests.contains("var configured = new JobsClientConfig(config);"), tests);
    assertTrue(
        tests.contains("return configured; }, static config => new JobsClient(config),"), tests);
    assertTrue(
        tests.contains(
            "client.GetJobAsync(new GetJobInput(Id: \"job-1\", Limit: 2), cancellationToken)"),
        tests);
    assertTrue(tests.contains("client.PingAsync(new PingInput(), cancellationToken)"), tests);
  }

  @Test
  void aFailureMatchesItsModeledErrorByTypeAndAnyOtherByErrorCode() throws Exception {
    String tests = generate(true);

    assertTrue(tests.contains("SmithySmokeTestExpectation.Success"), tests);
    assertTrue(
        tests.contains(
            "SmithySmokeTestExpectation.Failure(\"example.jobs#JobNotFound\", static exception"
                + " => exception is JobNotFound)"),
        tests);
    assertTrue(
        tests.contains(
            "exception => exception is SmithyClientException { ErrorCode: \"JobExpired\" }"),
        tests);
  }

  @Test
  void anyFailureMatchesAnUnmodeledErrorOrTheOperationsAndServicesErrors() throws Exception {
    String tests = generate(true);

    assertTrue(
        tests.contains(
            "Failure(null, static exception => exception is SmithyClientException or JobNotFound"
                + " or Throttled or ValidationException)"),
        tests);
  }

  @Test
  void smokeTestsAreOptIn() throws Exception {
    generateModel(false);

    assertFalse(Files.exists(tempDir.resolve("Example/Jobs/Jobs.SmokeTests.Client.g.cs")));
  }

  private String generate(boolean smokeTests) throws Exception {
    generateModel(smokeTests);
    // Whitespace-insensitive, since the output is formatted only where csharpier is installed.
    return Files.readString(tempDir.resolve("Example/Jobs/Jobs.SmokeTests.Client.g.cs"))
        .replaceAll("\\s+", " ")
        .replace("( ", "(")
        .replace(" )", ")");
  }

  private void generateModel(boolean smokeTests) {
    Model model =
        Model.assembler()
            .discoverModels(SmokeTestGeneratorTest.class.getClassLoader())
            .addUnparsedModel("jobs.smithy", MODEL)
            .assemble()
            .unwrap();
    new CSharpCodegenPlugin()
        .execute(
            PluginContext.builder()
                .model(model)
                .fileManifest(FileManifest.create(tempDir))
                .settings(
                    ObjectNode.builder()
                        .withMember("service", Node.from("example.jobs#Jobs"))
                        .withMember("baseNamespace", Node.from(""))
                        .withMember("generateServer", Node.from(false))
                        .withMember("generateSmokeTests", Node.from(smokeTests))
                        .build())
                .build());
  }
}

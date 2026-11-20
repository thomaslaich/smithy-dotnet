package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.golden.Golden;
import java.util.ArrayList;
import java.util.List;
import java.util.ServiceLoader;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;

/**
 * The F# generator's output for each fixture. It calls the generator without the formatter the
 * plugin runs afterwards, which depends on what the machine has installed.
 */
class FSharpGoldenTest {

  @TestFactory
  Stream<DynamicTest> matchesTheCheckedInOutput() {
    List<FSharpIntegration> integrations = new ArrayList<>();
    ServiceLoader.load(FSharpIntegration.class).forEach(integrations::add);
    return Golden.tests(
        "fsharp",
        (model, settings, manifest) ->
            FSharpCodegenPlugin.generate(
                model, FSharpSettings.fromNode(settings), manifest, integrations));
  }
}

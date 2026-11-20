package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.golden.Golden;
import java.util.ArrayList;
import java.util.List;
import java.util.ServiceLoader;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;

/**
 * The C# generator's output for each fixture, with the integrations a build discovers, AWS's
 * included. It calls the generator without the formatter the plugin runs afterwards, which depends
 * on what the machine has installed.
 */
class CSharpGoldenTest {

  @TestFactory
  Stream<DynamicTest> matchesTheCheckedInOutput() {
    List<CSharpIntegration> integrations = new ArrayList<>();
    ServiceLoader.load(CSharpIntegration.class).forEach(integrations::add);
    return Golden.tests(
        "csharp",
        (model, settings, manifest) ->
            CSharpCodegenPlugin.generate(
                model, CSharpSettings.fromNode(settings), manifest, integrations));
  }
}

package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.GeneratedCodeFormatter;
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
 * included, formatted with csharpier like the plugin does.
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
                model, CSharpSettings.fromNode(settings), manifest, integrations),
        dir ->
            List.of(
                "csharpier",
                "format",
                "--include-generated",
                "--ignore-path",
                GeneratedCodeFormatter.NULL_DEVICE,
                dir.toString()));
  }
}

package io.github.thomaslaich.nsmithy.codegen.core;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.logging.Logger;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Formats generated code with a .NET formatter when one is installed, either on the path or as a
 * {@code dotnet} tool. Without one, the files are left as generated.
 */
@SmithyInternalApi
public final class GeneratedCodeFormatter {

  private static final Logger LOGGER = Logger.getLogger(GeneratedCodeFormatter.class.getName());

  /** The null device, for formatter options that take a file to ignore. */
  public static final String NULL_DEVICE =
      System.getProperty("os.name", "").toLowerCase(Locale.ROOT).contains("win")
          ? "NUL"
          : "/dev/null";

  private GeneratedCodeFormatter() {}

  /** Runs {@code tool arguments}, falling back to {@code dotnet tool arguments}. */
  public static void format(String tool, List<String> arguments) {
    List<String> direct = new ArrayList<>();
    direct.add(tool);
    direct.addAll(arguments);
    List<String> viaDotnet = new ArrayList<>();
    viaDotnet.add("dotnet");
    viaDotnet.addAll(direct);
    for (List<String> cmd : List.of(direct, viaDotnet)) {
      try {
        int exit = new ProcessBuilder(cmd).redirectErrorStream(true).inheritIO().start().waitFor();
        if (exit == 0) return;
        LOGGER.fine(cmd + " exited with code " + exit + ", trying next");
      } catch (IOException | InterruptedException e) {
        LOGGER.fine(cmd + " not available: " + e.getMessage());
        if (e instanceof InterruptedException) Thread.currentThread().interrupt();
      }
    }
    LOGGER.fine(tool + " not found, generated files left unformatted");
  }
}

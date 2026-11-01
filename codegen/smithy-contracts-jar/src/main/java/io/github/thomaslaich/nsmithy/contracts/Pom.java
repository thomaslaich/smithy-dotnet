package io.github.thomaslaich.nsmithy.contracts;

import java.util.Collection;

/** Renders the contract's minimal Maven POM. */
final class Pom {

  private Pom() {}

  static String render(
      String groupId, String artifactId, String version, Collection<String> dependencies) {
    StringBuilder pom =
        new StringBuilder()
            .append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n")
            .append("<project xmlns=\"http://maven.apache.org/POM/4.0.0\"")
            .append(" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"")
            .append(" xsi:schemaLocation=\"http://maven.apache.org/POM/4.0.0")
            .append(" https://maven.apache.org/xsd/maven-4.0.0.xsd\">\n")
            .append("  <modelVersion>4.0.0</modelVersion>\n");
    element(pom, "  ", "groupId", groupId);
    element(pom, "  ", "artifactId", artifactId);
    element(pom, "  ", "version", version);
    element(pom, "  ", "packaging", "jar");
    if (!dependencies.isEmpty()) {
      pom.append("  <dependencies>\n");
      for (String coordinate : dependencies) {
        String[] parts = coordinate.split(":", -1);
        pom.append("    <dependency>\n");
        element(pom, "      ", "groupId", parts[0]);
        element(pom, "      ", "artifactId", parts[1]);
        element(pom, "      ", "version", parts[2]);
        pom.append("    </dependency>\n");
      }
      pom.append("  </dependencies>\n");
    }
    return pom.append("</project>\n").toString();
  }

  private static void element(StringBuilder pom, String indent, String name, String value) {
    pom.append(indent)
        .append('<')
        .append(name)
        .append('>')
        .append(escape(value))
        .append("</")
        .append(name)
        .append(">\n");
  }

  private static String escape(String value) {
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
  }
}

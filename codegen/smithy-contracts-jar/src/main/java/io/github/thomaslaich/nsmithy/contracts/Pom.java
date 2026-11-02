package io.github.thomaslaich.nsmithy.contracts;

import java.util.Collection;
import software.amazon.smithy.utils.SimpleCodeWriter;

/** Renders the contract's minimal Maven POM. */
final class Pom {

  private Pom() {}

  static String render(
      String groupId, String artifactId, String version, Collection<String> dependencies) {
    SimpleCodeWriter writer = new SimpleCodeWriter();
    writer.putContext("groupId", escape(groupId));
    writer.putContext("artifactId", escape(artifactId));
    writer.putContext("version", escape(version));
    writer.putContext("hasDependencies", !dependencies.isEmpty());
    writer.putContext(
        "dependencies", (Runnable) () -> dependencies.forEach(d -> writeDependency(writer, d)));
    writer.write(
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0" \
        xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" \
        xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 \
        https://maven.apache.org/xsd/maven-4.0.0.xsd">
          <modelVersion>4.0.0</modelVersion>
          <groupId>${groupId:L}</groupId>
          <artifactId>${artifactId:L}</artifactId>
          <version>${version:L}</version>
          <packaging>jar</packaging>
          ${?hasDependencies}
          <dependencies>
            ${dependencies:C|}
          </dependencies>
          ${/hasDependencies}
        </project>""");
    return writer.toString();
  }

  private static void writeDependency(SimpleCodeWriter writer, String coordinate) {
    String[] parts = coordinate.split(":", -1);
    writer.pushState();
    writer.putContext("groupId", escape(parts[0]));
    writer.putContext("artifactId", escape(parts[1]));
    writer.putContext("version", escape(parts[2]));
    writer.write(
        """
        <dependency>
          <groupId>${groupId:L}</groupId>
          <artifactId>${artifactId:L}</artifactId>
          <version>${version:L}</version>
        </dependency>""");
    writer.popState();
  }

  private static String escape(String value) {
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;");
  }
}

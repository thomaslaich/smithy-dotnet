package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class NamingTest {

  @Test
  void enumValueTurnsCapitalWordsIntoPascalCase() {
    assertEquals("Queued", Naming.enumValue("QUEUED"));
    assertEquals("InProgress", Naming.enumValue("IN_PROGRESS"));
    assertEquals("EuWest1", Naming.enumValue("EU_WEST_1"));
    assertEquals("EuWest1", Naming.enumValue("eu-west-1"));
  }

  @Test
  void enumValueKeepsMixedCaseWords() {
    assertEquals("Queued", Naming.enumValue("Queued"));
    assertEquals("InProgress", Naming.enumValue("inProgress"));
    assertEquals("TLSv12", Naming.enumValue("TLSv1_2"));
  }

  @Test
  void enumValueStartingWithADigitGetsAPrefix() {
    assertEquals("_1Day", Naming.enumValue("1_DAY"));
  }
}

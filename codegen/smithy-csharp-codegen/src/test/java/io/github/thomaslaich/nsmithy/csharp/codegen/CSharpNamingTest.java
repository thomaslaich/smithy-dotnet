package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class CSharpNamingTest {

  @Test
  void formatStringEscapesQuotesAndCommonControlCharacters() {
    assertEquals("\"a\\\"b\\\\c\\n\\r\\t\"", CSharpNaming.formatString("a\"b\\c\n\r\t"));
  }

  @Test
  void formatStringEscapesCharactersCSharpTreatsAsLineBreaks() {
    assertEquals("\"a\\u2028b\\u2029c\\u0085d\"", CSharpNaming.formatString("a b c\u0085d"));
  }

  @Test
  void formatStringEscapesOtherControlCharacters() {
    assertEquals("\"\\u001B\\u007F\"", CSharpNaming.formatString("\u001B\u007F"));
  }
}

package io.github.thomaslaich.nsmithy.codegen.core;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;

import java.util.List;
import org.junit.jupiter.api.Test;

final class XmlDocMarkupTest {

  @Test
  void plainTextIsEscapedLineByLine() {
    assertEquals(
        List.of("Gets &lt;forecast&gt; &amp; conditions.", "", "Second paragraph."),
        XmlDocMarkup.render("Gets <forecast> & conditions.\n\nSecond paragraph.\n"));
  }

  @Test
  void blankDocumentationRendersNothing() {
    assertEquals(List.of(), XmlDocMarkup.render(null));
    assertEquals(List.of(), XmlDocMarkup.render("  \n"));
  }

  @Test
  void aLoneParagraphIsBare() {
    assertEquals(
        List.of("The name of the exception."),
        XmlDocMarkup.render("<p>The name of the exception.</p>"));
  }

  @Test
  void paragraphsBecomeParaAndWhitespaceCollapses() {
    assertEquals(
        List.of("<para>", "First one.", "</para>", "<para>", "Second one.", "</para>"),
        XmlDocMarkup.render("<p>First\n   one.</p>\n\n  <p>Second one.</p>"));
  }

  @Test
  void inlineMarkupMapsToXmlDocTags() {
    assertEquals(
        List.of(
            "Set <c>Key</c> to <b>bold</b> or <i>italic</i>; see",
            "<see href=\"https://example.com/?a=1&amp;b=2\">docs</see>.<br/>Done."),
        XmlDocMarkup.render(
            "<p>Set <code>Key</code> to <b>bold</b> or <em>italic</em>; see"
                + " <a href=\"https://example.com/?a=1&amp;b=2\">docs</a>.<br>Done.</p>"));
  }

  @Test
  void unknownTagsArePlaceholdersAndStayLiteral() {
    assertEquals(
        List.of("The ARN is <c>arn:aws:s3:&lt;Region&gt;:&lt;account-id&gt;:accesspoint</c>."),
        XmlDocMarkup.render(
            "<p>The ARN is <code>arn:aws:s3:<Region>:<account-id>:accesspoint</code>.</p>"));
    assertEquals(
        List.of("&lt;ForecastExportJobName&gt;_&lt;ExportTimestamp&gt;"),
        XmlDocMarkup.render("<p><ForecastExportJobName>_<ExportTimestamp></p>"));
  }

  @Test
  void listsBecomeListElements() {
    assertEquals(
        List.of(
            "<para>",
            "Options:",
            "</para>",
            "<list type=\"bullet\">",
            "<item>",
            "<description>",
            "<c>$AccountName</c> is the name",
            "</description>",
            "</item>",
            "<item>",
            "<description>",
            "<para>",
            "Two paragraphs.",
            "</para>",
            "<para>",
            "In one item.",
            "</para>",
            "</description>",
            "</item>",
            "</list>",
            "<list type=\"number\">",
            "<item>",
            "<description>",
            "First",
            "</description>",
            "</item>",
            "</list>"),
        XmlDocMarkup.render(
            "<p>Options:</p> <ul> <li> <p> <code>$AccountName</code> is the name</p> </li>"
                + " <li><p>Two paragraphs.</p><p>In one item.</p></li> </ul>"
                + " <ol><li>First</li></ol>"));
  }

  @Test
  void definitionListsUseTerms() {
    assertEquals(
        List.of(
            "<list type=\"bullet\">",
            "<item>",
            "<term>Auto</term>",
            "<description>",
            "Chooses for you.",
            "</description>",
            "</item>",
            "<item>",
            "<term>Manual</term>",
            "<description>",
            "You choose.",
            "</description>",
            "</item>",
            "</list>"),
        XmlDocMarkup.render(
            "<dl> <dt>Auto</dt> <dd> <p>Chooses for you.</p> </dd>"
                + " <dt>Manual</dt> <dd>You choose.</dd> </dl>"));
  }

  @Test
  void notesGetALabel() {
    assertEquals(
        List.of(
            "<para>",
            "Body.",
            "</para>",
            "<para>",
            "<b>Note:</b> Be careful.",
            "</para>",
            "<para>",
            "<b>Important:</b> Really.",
            "</para>"),
        XmlDocMarkup.render(
            "<p>Body.</p> <note> <p>Be careful.</p> </note> <important>Really.</important>"));
  }

  @Test
  void headingsAreBold() {
    assertEquals(
        List.of("<para>", "<b>CodeBuild</b>", "</para>", "<para>", "Builds code.", "</para>"),
        XmlDocMarkup.render("<fullname>CodeBuild</fullname> <p>Builds code.</p>"));
  }

  @Test
  void preformattedTextKeepsItsLines() {
    assertEquals(
        List.of("<code>", "{", "  \"a\": \"&lt;b&gt;\"", "}", "</code>"),
        XmlDocMarkup.render("<pre>\n{\n  \"a\": \"&lt;b&gt;\"\n}\n</pre>"));
  }

  @Test
  void entitiesAreDecodedThenEscaped() {
    assertEquals(
        List.of("a &lt; b &amp; c &gt; d \"e\" –"),
        XmlDocMarkup.render("<p>a &lt; b &amp; c &gt; d &quot;e&quot;&nbsp;&#8211;</p>"));
  }

  @Test
  void emptyAndSelfClosingParagraphsAreDropped() {
    assertEquals(List.of("Text."), XmlDocMarkup.render("<p/><p>Text.</p><p></p>"));
  }

  @Test
  void unclosedParagraphsAreTolerated() {
    assertEquals(
        List.of("<para>", "One", "</para>", "<para>", "Two", "</para>"),
        XmlDocMarkup.render("<p>One<p>Two"));
  }

  @Test
  void longParagraphsWrap() {
    String word = "abcdefghij";
    String text = String.join(" ", java.util.Collections.nCopies(20, word));
    List<String> lines = XmlDocMarkup.render("<p>" + text + "</p>");
    assertEquals(3, lines.size());
    assertEquals(98, lines.get(0).length());
    assertEquals(text, String.join(" ", lines));
  }

  @Test
  void wrappingNeverBreaksInsideATag() {
    String filler = String.join(" ", java.util.Collections.nCopies(9, "abcdefghij"));
    List<String> lines =
        XmlDocMarkup.render(
            "<p>" + filler + " <a href=\"https://example.com/x\">link</a> tail</p>");
    assertEquals(List.of(filler, "<see href=\"https://example.com/x\">link</see> tail"), lines);
  }

  @Test
  void unicodeLineSeparatorsNeverReachTheOutput() {
    // The C# lexer ends a line at U+2028, which would end the /// comment mid-sentence.
    List<String> lines = XmlDocMarkup.render("<p>metric data\u2028 over a <code>period</code></p>");
    assertEquals(List.of("metric data over a <c>period</c>"), lines);
    assertEquals(List.of("one", "two"), XmlDocMarkup.render("one\u2029two"));
    for (String line : XmlDocMarkup.render("<p>a\u0085b</p>")) {
      assertFalse(line.chars().anyMatch(c -> c == 0x85 || c == 0x2028 || c == 0x2029));
    }
  }
}

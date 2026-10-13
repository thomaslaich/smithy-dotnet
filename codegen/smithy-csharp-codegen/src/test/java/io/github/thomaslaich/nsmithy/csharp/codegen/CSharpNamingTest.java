package io.github.thomaslaich.nsmithy.csharp.codegen;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;
import software.amazon.smithy.model.shapes.MemberShape;

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

  @Test
  void membersNamedLikeTheirTypeOrAnInheritedMemberTakeTheValueSuffix() {
    assertEquals("DomainNameValue", CSharpNaming.memberName(member("DomainName", "domainName")));
    assertEquals("EqualsValue", CSharpNaming.memberName(member("Selector", "equals")));
    assertEquals("Name", CSharpNaming.memberName(member("DomainName", "name")));
  }

  @Test
  void typesNamedLikeAnInheritedMemberTakeTheValueSuffix() {
    assertEquals("EqualsValue", CSharpNaming.typeName("Equals"));
    assertEquals("GetHashCodeValue", CSharpNaming.typeName("getHashCode"));
    assertEquals("Person", CSharpNaming.typeName("Person"));
  }

  @Test
  void stringEnumValuesAvoidTheEnumsOwnMembers() {
    assertEquals("ValueValue", CSharpNaming.stringEnumValueName(member("Operator", "Value")));
    assertEquals(
        "FromValueValue", CSharpNaming.stringEnumValueName(member("Operator", "FromValue")));
    assertEquals("EqualsValue", CSharpNaming.stringEnumValueName(member("Operator", "Equals")));
  }

  @Test
  void unionVariantsNamedLikeUnionMembersTakeTheCaseSuffix() {
    assertEquals("ValueCase", CSharpNaming.variantName(member("PathElement", "value")));
    assertEquals("TagCase", CSharpNaming.variantName(member("PathElement", "tag")));
    assertEquals("UnknownCase", CSharpNaming.variantName(member("PathElement", "unknown")));
    assertEquals("MatchCase", CSharpNaming.variantName(member("PathElement", "match")));
    assertEquals("EqualsCase", CSharpNaming.variantName(member("PathElement", "equals")));
    assertEquals("PathElementCase", CSharpNaming.variantName(member("PathElement", "pathElement")));
    assertEquals("Index", CSharpNaming.variantName(member("PathElement", "index")));
    assertEquals("FromValue", CSharpNaming.variantFactoryName(member("PathElement", "value")));
  }

  private static MemberShape member(String container, String name) {
    return MemberShape.builder()
        .id("example#" + container + "$" + name)
        .target("smithy.api#String")
        .build();
  }
}

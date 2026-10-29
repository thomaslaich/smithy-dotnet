package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.math.BigInteger;
import org.junit.jupiter.api.Test;

final class CSharpShapeSupportTest {

  @Test
  void decimalLiteralIsASingleConstantBeyondTheLongRange() {
    assertEquals(
        "-9223372036854775809m",
        CSharpShapeSupport.decimalLiteral(new BigInteger("-9223372036854775809")));
  }

  @Test
  void decimalLiteralSpellsOutExponents() {
    assertEquals("10000000000m", CSharpShapeSupport.decimalLiteral(1.0e10));
    assertEquals("1.5m", CSharpShapeSupport.decimalLiteral(1.5));
    assertEquals("42m", CSharpShapeSupport.decimalLiteral(42));
  }
}

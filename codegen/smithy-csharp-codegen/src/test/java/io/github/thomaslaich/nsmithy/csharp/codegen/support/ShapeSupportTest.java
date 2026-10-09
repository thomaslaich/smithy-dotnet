package io.github.thomaslaich.nsmithy.csharp.codegen.support;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.math.BigInteger;
import org.junit.jupiter.api.Test;

final class ShapeSupportTest {

  @Test
  void decimalLiteralIsASingleConstantBeyondTheLongRange() {
    assertEquals(
        "-9223372036854775809m",
        ShapeSupport.decimalLiteral(new BigInteger("-9223372036854775809")));
  }

  @Test
  void decimalLiteralSpellsOutExponents() {
    assertEquals("10000000000m", ShapeSupport.decimalLiteral(1.0e10));
    assertEquals("1.5m", ShapeSupport.decimalLiteral(1.5));
    assertEquals("42m", ShapeSupport.decimalLiteral(42));
  }
}

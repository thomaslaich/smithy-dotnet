/*
 * The C# shape of a rules engine value as generated endpoint resolvers hold it.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Map;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * A rules engine value type in generated C#: a string, boolean, integer, string list, or a record
 * returned by a library function. A record names its runtime type and maps each rules field to the
 * C# property that holds it.
 */
@SmithyUnstableApi
public record EndpointValueType(Kind kind, Symbol record, Map<String, Field> fields) {

  public enum Kind {
    STRING,
    BOOLEAN,
    INTEGER,
    STRING_LIST,
    RECORD
  }

  /** A record field: its C# property, type, and whether the property is nullable. */
  public record Field(String property, EndpointValueType type, boolean nullable) {}

  public static final EndpointValueType STRING = new EndpointValueType(Kind.STRING, null, Map.of());
  public static final EndpointValueType BOOLEAN =
      new EndpointValueType(Kind.BOOLEAN, null, Map.of());
  public static final EndpointValueType INTEGER =
      new EndpointValueType(Kind.INTEGER, null, Map.of());
  public static final EndpointValueType STRING_LIST =
      new EndpointValueType(Kind.STRING_LIST, null, Map.of());

  public static EndpointValueType record(Symbol symbol, Map<String, Field> fields) {
    return new EndpointValueType(Kind.RECORD, symbol, Map.copyOf(fields));
  }

  public static Field field(String property, EndpointValueType type) {
    return new Field(property, type, false);
  }

  public static Field nullableField(String property, EndpointValueType type) {
    return new Field(property, type, true);
  }

  /** Whether C# holds this type as a value type, so a nullable one needs {@code .Value}. */
  public boolean isValueType() {
    return kind == Kind.BOOLEAN || kind == Kind.INTEGER;
  }

  /** The C# type name, without nullability. */
  public String render(CSharpWriter writer) {
    return switch (kind) {
      case STRING -> "string";
      case BOOLEAN -> "bool";
      case INTEGER -> "int";
      case STRING_LIST -> writer.typeName(RuntimeTypes.I_READ_ONLY_LIST) + "<string>";
      case RECORD -> writer.typeName(record);
    };
  }
}

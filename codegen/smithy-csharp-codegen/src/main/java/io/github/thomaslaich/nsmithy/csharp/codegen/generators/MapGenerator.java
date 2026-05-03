/*
 * Renders a Smithy map as a C# wrapper record over IReadOnlyDictionary<TKey, TValue>.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class MapGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final MapShape shape;

  public MapGenerator(GenerationContext c, CSharpWriter w, MapShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    Symbol key = sp.toSymbol(context.model().expectShape(shape.getKey().getTarget()));
    Symbol value = sp.toSymbol(context.model().expectShape(shape.getValue().getTarget()));
    String keyType = CSharpSymbolProvider.qualified(key);
    String valueType =
        CSharpSymbolProvider.qualified(value) + (ShapeSupport.isSparse(shape) ? "?" : "");

    writer.write(
        "public sealed partial record class $L : ISerializableShape, IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          SchemaGenerator.writeMapSchema(writer, context, shape);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writer.write(
              "public $L(System.Collections.Generic.IReadOnlyDictionary<$L, $L> values)",
              typeName,
              keyType,
              valueType);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(values);");
                writer.write(
                    "Values = new System.Collections.ObjectModel.ReadOnlyDictionary<$L, $L>("
                        + "new System.Collections.Generic.Dictionary<$L, $L>(values));",
                    keyType,
                    valueType,
                    keyType,
                    valueType);
              });
          writer.write("");
          writer.write(
              "public System.Collections.Generic.IReadOnlyDictionary<$L, $L> Values { get; }",
              keyType,
              valueType);
          writer.write("");
          writeSerialize();
          writer.write("");
          writeDeserialize(typeName, keyType, valueType);
        });
  }

  private void writeSerialize() {
    SymbolProvider sp = context.symbolProvider();
    Shape valueTarget = context.model().expectShape(shape.getValue().getTarget());
    Symbol valueSym = sp.toSymbol(valueTarget);
    boolean valueIsValueType =
        valueSym.getProperty(SymbolProperties.IS_VALUE_TYPE, Boolean.class).orElse(false);
    String valueTypeName = CSharpSymbolProvider.qualified(valueSym);
    writer.write("public void Serialize(IShapeSerializer serializer)");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
          writer.write("serializer.WriteMap(Schema, Values, Values.Count, static (values, m) =>");
          writer.openBlock(
              "{",
              "});",
              () -> {
                writer.write("foreach (var entry in values)");
                writer.openBlock(
                    "{",
                    "}",
                    () -> {
                      if (ShapeSupport.isSparse(shape)) {
                        writer.write("if (entry.Value is null)");
                        writer.openBlock(
                            "{",
                            "}",
                            () -> {
                              writer.write(
                                  "m.Entry(entry.Key, ValueSchema, static (schema, w) =>"
                                      + " w.WriteNull(schema));");
                            });
                        writer.write("else");
                        writer.openBlock(
                            "{",
                            "}",
                            () -> {
                              // For nullable value types (structs) in sparse maps, we must cast
                              // entry.Value (T?) to T explicitly so the generic lambda types
                              // correctly.
                              String stateExpr =
                                  valueIsValueType
                                      ? "(" + valueTypeName + ")entry.Value!"
                                      : "entry.Value";
                              writer.write(
                                  "m.Entry(entry.Key, $L, static (value, w) =>"
                                      + " "
                                      + stripTrailingSemicolon(
                                          writeValueStatement(
                                              valueTarget, "w", "ValueSchema", "value"))
                                      + ");",
                                  stateExpr);
                            });
                      } else {
                        writer.write(
                            "m.Entry(entry.Key, entry.Value, static (value, w) =>"
                                + " "
                                + stripTrailingSemicolon(
                                    writeValueStatement(valueTarget, "w", "ValueSchema", "value"))
                                + ");");
                      }
                    });
              });
        });
  }

  private void writeDeserialize(String typeName, String keyType, String valueType) {
    Shape valueTarget = context.model().expectShape(shape.getValue().getTarget());
    writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
          writer.write(
              "var values = new System.Collections.Generic.Dictionary<$L, $L>("
                  + "System.StringComparer.Ordinal);",
              keyType,
              valueType);
          writer.write("deserializer.ReadMap(Schema, values, static (map, key, r) =>");
          writer.openBlock(
              "{",
              "});",
              () -> {
                if (ShapeSupport.isSparse(shape)) {
                  writer.write("if (r.IsNull())");
                  writer.openBlock(
                      "{",
                      "}",
                      () -> {
                        writer.write("r.ReadNull();");
                        writer.write("map[key] = null;");
                      });
                  writer.write("else");
                  writer.openBlock(
                      "{",
                      "}",
                      () ->
                          writer.write(
                              "map[key] = "
                                  + readValueExpression(valueTarget, "r", "ValueSchema")
                                  + ";"));
                } else {
                  writer.write(
                      "map[key] = " + readValueExpression(valueTarget, "r", "ValueSchema") + ";");
                }
              });
          writer.write("return new $L(values);", typeName);
        });
  }

  private String writeValueStatement(
      Shape target, String serializerVar, String schemaVar, String valueExpr) {
    return switch (target.getType()) {
      case BOOLEAN -> serializerVar + ".WriteBoolean(" + schemaVar + ", " + valueExpr + ")";
      case BYTE -> serializerVar + ".WriteByte(" + schemaVar + ", " + valueExpr + ")";
      case SHORT -> serializerVar + ".WriteShort(" + schemaVar + ", " + valueExpr + ")";
      case INTEGER -> serializerVar + ".WriteInteger(" + schemaVar + ", " + valueExpr + ")";
      case LONG -> serializerVar + ".WriteLong(" + schemaVar + ", " + valueExpr + ")";
      case FLOAT -> serializerVar + ".WriteFloat(" + schemaVar + ", " + valueExpr + ")";
      case DOUBLE -> serializerVar + ".WriteDouble(" + schemaVar + ", " + valueExpr + ")";
      case BIG_INTEGER -> serializerVar + ".WriteBigInteger(" + schemaVar + ", " + valueExpr + ")";
      case BIG_DECIMAL -> serializerVar + ".WriteBigDecimal(" + schemaVar + ", " + valueExpr + ")";
      case TIMESTAMP -> serializerVar + ".WriteTimestamp(" + schemaVar + ", " + valueExpr + ")";
      case STRING -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ")";
      case ENUM -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ".Value)";
      case BLOB -> serializerVar + ".WriteBlob(" + schemaVar + ", " + valueExpr + ")";
      case DOCUMENT -> serializerVar + ".WriteDocument(" + schemaVar + ", " + valueExpr + ")";
      case INT_ENUM -> serializerVar + ".WriteInteger(" + schemaVar + ", (int)" + valueExpr + ")";
      case STRUCTURE, UNION, LIST, SET, MAP -> valueExpr + ".Serialize(" + serializerVar + ");";
      default ->
          throw new IllegalArgumentException("Unsupported map value shape: " + target.getId());
    };
  }

  private static String stripTrailingSemicolon(String statement) {
    return statement.endsWith(";")
        ? statement.substring(0, statement.length() - 1)
        : statement;
  }

  private String readValueExpression(Shape target, String deserializerVar, String schemaVar) {
    return switch (target.getType()) {
      case BOOLEAN -> deserializerVar + ".ReadBoolean(" + schemaVar + ")";
      case BYTE -> deserializerVar + ".ReadByte(" + schemaVar + ")";
      case SHORT -> deserializerVar + ".ReadShort(" + schemaVar + ")";
      case INTEGER -> deserializerVar + ".ReadInteger(" + schemaVar + ")";
      case LONG -> deserializerVar + ".ReadLong(" + schemaVar + ")";
      case FLOAT -> deserializerVar + ".ReadFloat(" + schemaVar + ")";
      case DOUBLE -> deserializerVar + ".ReadDouble(" + schemaVar + ")";
      case BIG_INTEGER -> deserializerVar + ".ReadBigInteger(" + schemaVar + ")";
      case BIG_DECIMAL -> deserializerVar + ".ReadBigDecimal(" + schemaVar + ")";
      case TIMESTAMP -> deserializerVar + ".ReadTimestamp(" + schemaVar + ")";
      case STRING -> deserializerVar + ".ReadString(" + schemaVar + ")";
      case BLOB -> deserializerVar + ".ReadBlob(" + schemaVar + ")";
      case DOCUMENT -> deserializerVar + ".ReadDocument(" + schemaVar + ")";
      case ENUM, STRUCTURE, UNION, LIST, SET, MAP ->
          CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ".Deserialize("
              + deserializerVar
              + ")";
      case INT_ENUM ->
          "("
              + CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ")"
              + deserializerVar
              + ".ReadInteger("
              + schemaVar
              + ")";
      default ->
          throw new IllegalArgumentException("Unsupported map value shape: " + target.getId());
    };
  }
}

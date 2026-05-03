/*
 * Renders a Smithy string-typed enum as a C# `readonly partial record struct`
 * holding the underlying string value, with a static property per variant.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class StringEnumGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final EnumShape shape;

  public StringEnumGenerator(GenerationContext c, CSharpWriter w, EnumShape s) {
    this.context = c;
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    writer.addImport(RuntimeTypes.NSMITHY_CORE_ANNOTATIONS);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.write(
        "public readonly partial record struct $L(string Value) : ISerializableShape,"
            + " IDeserializableShape<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          SchemaGenerator.writeSimpleSchema(writer, shape);
          writer.write("Schema ISerializableShape.Schema => Schema;");
          writer.write("");
          writer.write("public void Serialize(IShapeSerializer serializer)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(serializer);");
                writer.write("serializer.WriteString(Schema, Value);");
              });
          writer.write("");
          writer.write("public static $L Deserialize(IShapeDeserializer deserializer)", typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(deserializer);");
                writer.write("return new $L(deserializer.ReadString(Schema));", typeName);
              });
          writer.write("");
          for (MemberShape m : ShapeSupport.sortedMembers(shape)) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            String value =
                m.getTrait(EnumValueTrait.class)
                    .flatMap(t -> t.getStringValue())
                    .orElse(m.getMemberName());
            writer.write("[SmithyEnumValue($L)]", CSharpNaming.formatString(value));
            writer.write(
                "public static $L $L { get; } = new($L);",
                typeName,
                prop,
                CSharpNaming.formatString(value));
          }
          writer.write("");
          writer.write("public override string ToString()");
          writer.openBlock("{", "}", () -> writer.write("return Value;"));
        });
  }
}

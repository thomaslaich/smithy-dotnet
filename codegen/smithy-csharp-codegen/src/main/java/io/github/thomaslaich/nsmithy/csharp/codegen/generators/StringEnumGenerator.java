/*
 * Renders a Smithy string-typed enum as a C# `readonly partial record struct`
 * holding the underlying string value, with a static property per variant.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
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
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    writer.write(
        "public readonly partial record struct $L(string Value) : IStringEnumValue<$L>",
        typeName,
        typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static $L FromValue(string value)", typeName);
          writer.openBlock("{", "}", () -> writer.write("return new $L(value);", typeName));
          writer.write("");
          for (MemberShape m : ShapeSupport.sortedMembers(shape)) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            String value =
                m.getTrait(EnumValueTrait.class)
                    .flatMap(t -> t.getStringValue())
                    .orElse(m.getMemberName());
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
    writer.write("");
    SchemaGenerator.writeSimpleSchema(writer, shape);
  }
}

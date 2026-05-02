/*
 * Renders a Smithy string-typed enum as a C# `readonly partial record struct`
 * holding the underlying string value, with a static property per variant.
 */
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
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
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "public readonly partial record struct $L(string Value) {",
        "}",
        typeName,
        () -> {
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
          writer.openBlock(
              "public override string ToString() {", "}", () -> writer.write("return Value;"));
        });
  }
}

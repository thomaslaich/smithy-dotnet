/*
 * Renders a Smithy intEnum as a C# `enum`.
 */
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class IntEnumGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final IntEnumShape shape;

  public IntEnumGenerator(GenerationContext c, CSharpWriter w, IntEnumShape s) {
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
        "public enum $L {",
        "}",
        typeName,
        () -> {
          for (MemberShape m : ShapeSupport.sortedMembers(shape)) {
            String prop = CSharpNaming.propertyName(m.getMemberName());
            Integer value =
                m.getTrait(EnumValueTrait.class).flatMap(t -> t.getIntValue()).orElse(null);
            if (value != null) {
              writer.write(
                  "[SmithyEnumValue($L)]", CSharpNaming.formatString(Integer.toString(value)));
              writer.write("$L = $L,", prop, value);
            } else {
              writer.write("$L,", prop);
            }
          }
        });
  }
}

/*
 * Renders a Smithy intEnum as a C# `enum`.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class IntEnumGenerator implements Runnable {

  private final CSharpWriter writer;
  private final IntEnumShape shape;

  public IntEnumGenerator(CSharpWriter w, IntEnumShape s) {
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("variants", writer.consumer(w -> writeVariants()));
      writer.writeXmlDocs(shape);
      writer.write(
          """
          public enum ${typeName:L}
          {
              ${variants:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeVariants() {
    for (MemberShape member : ShapeSupport.sortedMembers(shape)) {
      String property = CSharpNaming.propertyName(member.getMemberName());
      Integer value =
          member.getTrait(EnumValueTrait.class).flatMap(EnumValueTrait::getIntValue).orElse(null);
      writer.writeXmlDocs(member);
      if (value != null) {
        writer.write("$L = $L,", property, value);
      } else {
        writer.write("$L,", property);
      }
    }
  }
}

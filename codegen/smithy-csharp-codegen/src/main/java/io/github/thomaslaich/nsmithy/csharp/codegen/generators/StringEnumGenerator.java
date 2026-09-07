/*
 * Renders a Smithy string-typed enum as a C# `readonly partial record struct`
 * holding the underlying string value, with a static property per variant.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class StringEnumGenerator implements Runnable {

  private final CSharpWriter writer;
  private final EnumShape shape;

  public StringEnumGenerator(CSharpWriter w, EnumShape s) {
    this.writer = w;
    this.shape = s;
  }

  @Override
  public void run() {
    String typeName = CSharpNaming.typeName(shape.getId().getName());
    writer.pushState();
    try {
      writer.putContext("typeName", typeName);
      writer.putContext("stringEnumValue", RuntimeTypes.I_STRING_ENUM_VALUE);
      writer.putContext("variants", writer.consumer(w -> writeVariants(typeName)));
      writer.writeXmlDocs(shape);
      writer.write(
          """
          public readonly partial record struct ${typeName:L}(string Value)
              : ${stringEnumValue:T}<${typeName:L}>
          {
              public static ${typeName:L} FromValue(string value)
              {
                  return new ${typeName:L}(value);
              }

              ${variants:C|}

              public override string ToString()
              {
                  return Value;
              }
          }
          """);
    } finally {
      writer.popState();
    }
    writer.write("");
    SchemaGenerator.writeSimpleSchema(writer, shape);
  }

  private void writeVariants(String typeName) {
    for (MemberShape member : ShapeSupport.sortedMembers(shape)) {
      String property = CSharpNaming.propertyName(member.getMemberName());
      String value =
          member
              .getTrait(EnumValueTrait.class)
              .flatMap(EnumValueTrait::getStringValue)
              .orElse(member.getMemberName());
      writer.writeXmlDocs(member);
      writer.write(
          "public static $L $L { get; } = new($L);",
          typeName,
          property,
          CSharpNaming.formatString(value));
    }
  }
}

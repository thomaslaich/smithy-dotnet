/*
 * Declares the F# type of each shape:
 *
 *   structure  a record, or a class with no members when the structure has none
 *   @error     a class deriving from Exception, with a constructor that takes optional members
 *              as optional arguments
 *   union      a discriminated union, with an Unknown case for one this model does not define
 *   enum       a discriminated union with an Unknown case, and the value each case stands for
 *   intEnum    an F# enum
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.RetryableTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class TypeGenerator {

  private final FSharpContext context;

  TypeGenerator(FSharpContext context) {
    this.context = context;
  }

  void structure(StructureShape shape) {
    FSharpWriter writer = context.files().types(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    writer.writeDocs(shape);
    if (shape.members().isEmpty()) {
      writer.write(
          """
          [<Sealed>]
          type $1L() =
              override _.Equals(other) = other :? $1L
              override _.GetHashCode() = 0
          """,
          name);
      return;
    }
    writer.write("type $L =", name);
    writer.indent().write("{").indent();
    for (MemberShape member : shape.members()) {
      writeMemberDocs(writer, member);
      writer.write(
          "$L: $L", FSharpNaming.fieldName(member), FSharpMembers.fieldType(context, member));
    }
    writer.dedent().write("}").dedent().write("");
  }

  void error(StructureShape shape) {
    FSharpWriter writer = context.files().types(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    Optional<MemberShape> message = ShapeSupport.errorMessageMember(context.model(), shape);

    List<String> parameters = new ArrayList<>();
    for (MemberShape member : constructorOrder(shape)) {
      String parameter = FSharpNaming.parameterName(errorPropertyName(member, message));
      parameters.add(
          FSharpMembers.isOptional(member) || FSharpMembers.usesDefault(member)
              ? "?" + parameter + ": " + FSharpMembers.valueType(context, member)
              : parameter + ": " + FSharpMembers.valueType(context, member));
    }

    writer.writeDocs(shape);
    writer.write("type $L($L) =", name, String.join(", ", parameters));
    writer.indent();
    writer.write("inherit global.System.Exception($L)", message.map(this::argument).orElse(""));
    for (MemberShape member : shape.members()) {
      writeMemberDocs(writer, member);
      writer.write(
          "member val $L: $L = $L",
          errorPropertyName(member, message),
          FSharpMembers.fieldType(context, member),
          argument(member, message));
    }
    shape
        .getTrait(RetryableTrait.class)
        .ifPresent(
            trait ->
                writer.write(
                    """
                    interface global.NSmithy.Core.ISmithyRetryableError with
                        member _.IsThrottlingError = $L\
                    """,
                    trait.getThrottling()));
    writer.dedent().write("");
  }

  void union(UnionShape shape) {
    FSharpWriter writer = context.files().types(shape);
    writer.writeDocs(shape);
    writer.write("[<RequireQualifiedAccess>]");
    writer.write("type $L =", FSharpNaming.typeName(shape.getId().getName()));
    writer.indent();
    for (MemberShape member : shape.members()) {
      writeMemberDocs(writer, member);
      if (ShapeSupport.isUnit(member.getTarget())) {
        writer.write("| $L", FSharpNaming.caseName(member));
      } else {
        writer.write(
            "| $L of $L",
            FSharpNaming.caseName(member),
            context.symbolProvider().typeOf(context.model().expectShape(member.getTarget())));
      }
    }
    writer.write("/// <summary>A case this model does not define, as it was read.</summary>");
    writer.write("| Unknown of tag: string * value: $L", FSharpLiterals.DOCUMENT);
    writer.dedent().write("");
  }

  void stringEnum(EnumShape shape) {
    FSharpWriter writer = context.files().types(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    Map<String, String> values = shape.getEnumValues();
    writer.writeDocs(shape);
    writer.write("[<RequireQualifiedAccess>]");
    writer.write("type $L =", name);
    writer.indent();
    for (MemberShape member : shape.members()) {
      writeMemberDocs(writer, member);
      writer.write("| $L", FSharpNaming.caseName(member));
    }
    writer.write("/// <summary>A value this model does not define.</summary>");
    writer.write("| Unknown of value: string");
    writer.write("");
    writer.write("/// <summary>The value this case stands for on the wire.</summary>");
    writer.write("member this.Value =").indent().write("match this with");
    for (MemberShape member : shape.members()) {
      writer.write(
          "| $L.$L -> $L",
          name,
          FSharpNaming.caseName(member),
          FSharpNaming.stringLiteral(values.get(member.getMemberName())));
    }
    writer.write("| $L.Unknown value -> value", name).dedent();
    writer.write("");
    writer.write(
        "/// <summary>The case for a value, or <c>Unknown</c> for one this model does not"
            + " define.</summary>");
    writer.write("static member FromValue(value: string) =").indent().write("match value with");
    for (MemberShape member : shape.members()) {
      writer.write(
          "| $L -> $L.$L",
          FSharpNaming.stringLiteral(values.get(member.getMemberName())),
          name,
          FSharpNaming.caseName(member));
    }
    writer.write("| value -> $L.Unknown value", name).dedent();
    writer.write("");
    writer.write("override this.ToString() = this.Value");
    writer.write("");
    writer.write(
        """
        interface global.NSmithy.Core.Serde.IStringEnumValue<$1L> with
            member this.Value = this.Value
            static member FromValue(value) = $1L.FromValue(value)
        """,
        name);
    writer.dedent();
  }

  void intEnum(IntEnumShape shape) {
    FSharpWriter writer = context.files().types(shape);
    Map<String, Integer> values = shape.getEnumValues();
    writer.writeDocs(shape);
    writer.write("type $L =", FSharpNaming.typeName(shape.getId().getName()));
    writer.indent();
    for (MemberShape member : shape.members()) {
      writeMemberDocs(writer, member);
      int value = values.get(member.getMemberName());
      writer.write(
          "| $L = $L",
          FSharpNaming.intEnumValueName(member),
          value < 0 ? "(" + value + ")" : String.valueOf(value));
    }
    writer.dedent().write("");
  }

  /** The order of an error's constructor parameters: required members, then optional ones. */
  static List<MemberShape> constructorOrder(StructureShape shape) {
    List<MemberShape> order = new ArrayList<>();
    for (MemberShape member : shape.members()) {
      if (!FSharpMembers.isOptional(member) && !FSharpMembers.usesDefault(member)) {
        order.add(member);
      }
    }
    for (MemberShape member : shape.members()) {
      if (FSharpMembers.isOptional(member) || FSharpMembers.usesDefault(member)) {
        order.add(member);
      }
    }
    return order;
  }

  /** An error's property: {@code ErrorMessage} for its message member. */
  static String errorPropertyName(MemberShape member, Optional<MemberShape> message) {
    return message.isPresent() && message.get().equals(member)
        ? FSharpNaming.ERROR_MESSAGE
        : FSharpNaming.errorPropertyName(member);
  }

  /** The exception message: the message member's text, or null when it has none. */
  private String argument(MemberShape message) {
    String parameter = FSharpNaming.parameterName(FSharpNaming.ERROR_MESSAGE);
    if (FSharpMembers.isOptional(message)) {
      return "global.Microsoft.FSharp.Core.Option.toObj " + parameter;
    }
    if (FSharpMembers.usesDefault(message)) {
      return "global.Microsoft.FSharp.Core.Operators.defaultArg "
          + parameter
          + " ("
          + FSharpLiterals.defaultValue(context, message)
          + ")";
    }
    return parameter;
  }

  /** The value an error property holds: its argument, or its default when one is left out. */
  private String argument(MemberShape member, Optional<MemberShape> message) {
    String parameter = FSharpNaming.parameterName(errorPropertyName(member, message));
    if (FSharpMembers.usesDefault(member)) {
      return "global.Microsoft.FSharp.Core.Operators.defaultArg "
          + parameter
          + " ("
          + FSharpLiterals.defaultValue(context, member)
          + ")";
    }
    return parameter;
  }

  private void writeMemberDocs(FSharpWriter writer, MemberShape member) {
    ShapeSupport.memberDocumentation(context.model(), member).ifPresent(writer::writeDocs);
  }
}

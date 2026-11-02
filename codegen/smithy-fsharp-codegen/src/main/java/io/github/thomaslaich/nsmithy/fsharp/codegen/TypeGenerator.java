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
import java.util.LinkedHashMap;
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
    List<String> fields = new ArrayList<>();
    for (MemberShape member : shape.members()) {
      fields.add(
          documented(
              member,
              FSharpNaming.fieldName(member) + ": " + FSharpMembers.fieldType(context, member)));
    }
    context
        .files()
        .types(shape)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            ${^fields}
            [<Sealed>]
            type ${name:L}() =
                override _.Equals(other) = other :? ${name:L}
                override _.GetHashCode() = 0
            ${/fields}
            ${?fields}
            type ${name:L} =
                {
                    ${#fields}
                    ${value:L|}
                    ${/fields}
                }
            ${/fields}
            """,
            Map.of(
                "docs", FSharpWriter.docs(shape),
                "name", FSharpNaming.typeName(shape.getId().getName()),
                "fields", fields));
  }

  void error(StructureShape shape) {
    Optional<MemberShape> message = ShapeSupport.errorMessageMember(context.model(), shape);

    List<String> parameters = new ArrayList<>();
    for (MemberShape member : constructorOrder(shape)) {
      String parameter = FSharpNaming.parameterName(errorPropertyName(member, message));
      parameters.add(
          FSharpMembers.isOptional(member) || FSharpMembers.usesDefault(member)
              ? "?" + parameter + ": " + FSharpMembers.valueType(context, member)
              : parameter + ": " + FSharpMembers.valueType(context, member));
    }
    List<String> properties = new ArrayList<>();
    for (MemberShape member : shape.members()) {
      properties.add(
          documented(
              member,
              "member val "
                  + errorPropertyName(member, message)
                  + ": "
                  + FSharpMembers.fieldType(context, member)
                  + " = "
                  + argument(member, message)));
    }
    Optional<RetryableTrait> retryable = shape.getTrait(RetryableTrait.class);
    context
        .files()
        .types(shape)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            type ${name:L}(${parameters:L}) =
                inherit global.System.Exception(${message:L})
                ${#properties}
                ${value:L|}
                ${/properties}
                ${?retryable}
                interface global.NSmithy.Core.ISmithyRetryableError with
                    member _.IsThrottlingError = ${throttling:L}
                ${/retryable}
            """,
            Map.of(
                "docs", FSharpWriter.docs(shape),
                "name", FSharpNaming.typeName(shape.getId().getName()),
                "parameters", String.join(", ", parameters),
                "message", message.map(this::argument).orElse(""),
                "properties", properties,
                "retryable", retryable.isPresent(),
                "throttling", retryable.map(RetryableTrait::getThrottling).orElse(false)));
  }

  void union(UnionShape shape) {
    List<String> cases = new ArrayList<>();
    for (MemberShape member : shape.members()) {
      cases.add(
          documented(
              member,
              ShapeSupport.isUnit(member.getTarget())
                  ? "| " + FSharpNaming.caseName(member)
                  : "| "
                      + FSharpNaming.caseName(member)
                      + " of "
                      + context
                          .symbolProvider()
                          .typeOf(context.model().expectShape(member.getTarget()))));
    }
    context
        .files()
        .types(shape)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            [<RequireQualifiedAccess>]
            type ${name:L} =
                ${#cases}
                ${value:L|}
                ${/cases}
                /// <summary>A case this model does not define, as it was read.</summary>
                | Unknown of tag: string * value: ${document:L}
            """,
            Map.of(
                "docs",
                FSharpWriter.docs(shape),
                "name",
                FSharpNaming.typeName(shape.getId().getName()),
                "cases",
                cases,
                "document",
                FSharpLiterals.DOCUMENT));
  }

  void stringEnum(EnumShape shape) {
    Map<String, String> enumValues = shape.getEnumValues();
    List<String> cases = new ArrayList<>();
    Map<String, String> values = new LinkedHashMap<>();
    for (MemberShape member : shape.members()) {
      cases.add(documented(member, "| " + FSharpNaming.caseName(member)));
      values.put(
          FSharpNaming.caseName(member),
          FSharpNaming.stringLiteral(enumValues.get(member.getMemberName())));
    }
    context
        .files()
        .types(shape)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            [<RequireQualifiedAccess>]
            type ${name:L} =
                ${#cases}
                ${value:L|}
                ${/cases}
                /// <summary>A value this model does not define.</summary>
                | Unknown of value: string

                /// <summary>The value this case stands for on the wire.</summary>
                member this.Value =
                    match this with
                    ${#values}
                    | ${name:L}.${key:L} -> ${value:L}
                    ${/values}
                    | ${name:L}.Unknown value -> value

                /// <summary>The case for a value, or <c>Unknown</c> for one this model does not define.</summary>
                static member FromValue(value: string) =
                    match value with
                    ${#values}
                    | ${value:L} -> ${name:L}.${key:L}
                    ${/values}
                    | value -> ${name:L}.Unknown value

                override this.ToString() = this.Value

                interface global.NSmithy.Core.Serde.IStringEnumValue<${name:L}> with
                    member this.Value = this.Value
                    static member FromValue(value) = ${name:L}.FromValue(value)
            """,
            Map.of(
                "docs",
                FSharpWriter.docs(shape),
                "name",
                FSharpNaming.typeName(shape.getId().getName()),
                "cases",
                cases,
                "values",
                values));
  }

  void intEnum(IntEnumShape shape) {
    Map<String, Integer> values = shape.getEnumValues();
    List<String> cases = new ArrayList<>();
    for (MemberShape member : shape.members()) {
      int value = values.get(member.getMemberName());
      cases.add(
          documented(
              member,
              "| "
                  + FSharpNaming.intEnumValueName(member)
                  + " = "
                  + (value < 0 ? "(" + value + ")" : String.valueOf(value))));
    }
    context
        .files()
        .types(shape)
        .writeTemplate(
            """
            ${?docs}
            ${docs:L|}
            ${/docs}
            type ${name:L} =
                ${#cases}
                ${value:L|}
                ${/cases}
            """,
            Map.of(
                "docs", FSharpWriter.docs(shape),
                "name", FSharpNaming.typeName(shape.getId().getName()),
                "cases", cases));
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

  /** A member's code with the member's documentation, if any, on the lines before it. */
  private String documented(MemberShape member, String code) {
    return FSharpWriter.documented(
        ShapeSupport.memberDocumentation(context.model(), member)
            .map(FSharpWriter::docs)
            .orElse(""),
        code);
  }
}

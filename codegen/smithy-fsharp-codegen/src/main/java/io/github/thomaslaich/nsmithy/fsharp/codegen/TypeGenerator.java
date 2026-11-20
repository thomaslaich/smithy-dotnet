/*
 * Declares the F# type of each shape:
 *
 *   structure  a record, or a class with no members when the structure has none
 *   @error     a record like a structure's, and an exception class of the same name in the child
 *              Exceptions namespace that carries it through the runtime
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
    record(shape, false);
  }

  /**
   * A structure's record. An error's record requires qualified field labels, so its fields never
   * capture the inference of another record's, such as an input's {@code { Id = ... }}; where its
   * type is known, as in a union case or a pattern, labels need no qualifier.
   */
  private void record(StructureShape shape, boolean qualified) {
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
            ${?qualified}
            [<RequireQualifiedAccess>]
            ${/qualified}
            type ${name:L} =
                {
                    ${#fields}
                    ${value:L|}
                    ${/fields}
                }
            ${/fields}
            """,
            Map.of(
                "docs",
                FSharpWriter.docs(shape),
                "name",
                FSharpNaming.typeName(shape.getId().getName()),
                "fields",
                fields,
                "qualified",
                qualified));
  }

  /**
   * An error: a record like any structure, and, in the child Exceptions namespace, the exception
   * class that carries it through the runtime, which reports modeled errors as exceptions.
   */
  void error(StructureShape shape) {
    record(shape, true);
    Optional<MemberShape> message = ShapeSupport.errorMessageMember(context.model(), shape);
    Optional<RetryableTrait> retryable = shape.getTrait(RetryableTrait.class);
    String record = context.symbolProvider().typeOf(shape);
    context
        .files()
        .exceptions(shape)
        .writeTemplate(
            """
            /// <summary>Carries a <c>${name:L}</c> through the runtime, which reports modeled errors as exceptions.</summary>
            [<global.System.ComponentModel.EditorBrowsable(global.System.ComponentModel.EditorBrowsableState.Never)>]
            type ${name:L}(error: ${record:L}) =
                inherit global.System.Exception(${message:L})

                /// <summary>The error.</summary>
                member _.Error = error
                ${?retryable}

                interface global.NSmithy.Core.ISmithyRetryableError with
                    member _.IsThrottlingError = ${throttling:L}
                ${/retryable}
            """,
            Map.of(
                "name", FSharpNaming.typeName(shape.getId().getName()),
                "record", record,
                "message", message.map(this::message).orElse(""),
                "retryable", retryable.isPresent(),
                "throttling", retryable.map(RetryableTrait::getThrottling).orElse(false)));
  }

  /** The exception message: the message member's text, or null when it has none. */
  private String message(MemberShape member) {
    String field = "error." + FSharpNaming.fieldName(member);
    return FSharpMembers.isOptional(member)
        ? "global.Microsoft.FSharp.Core.Option.toObj " + field
        : field;
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

  /** A member's code with the member's documentation, if any, on the lines before it. */
  private String documented(MemberShape member, String code) {
    return FSharpWriter.documented(
        ShapeSupport.memberDocumentation(context.model(), member)
            .map(FSharpWriter::docs)
            .orElse(""),
        code);
  }
}

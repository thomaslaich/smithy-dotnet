/*
 * Declares the runtime schema of each shape in the shape's Schemas namespace.
 *
 * A structure, union, list, or map gets a class deriving from the runtime's AccessorStructSchema,
 * AccessorUnionSchema, ElementListSchema, or EntryMapSchema, and a holder type whose Schema property
 * is the one instance:
 *
 *   PersonBuilder      what a deserializer fills in, one value option per member
 *   PersonSchemaImpl   the schema class (internal)
 *   PersonSchema       the holder: PersonSchema.Schema
 *
 * F# cannot override a method whose type parameter allows a ref struct, so the runtime bases own
 * the generic reading and writing: a schema class overrides only the non-generic members (building
 * a value, telling union cases apart) and hands each member over as an accessor, a pair of
 * functions that get it from a value and set it on a builder.
 *
 * F# initializes a file's static values in the order they are declared, so a schema refers to
 * another structure, union, list, or map through Schemas.Lazy, which resolves it once every
 * schema is initialized. Enum schemas are declared first in their namespace and referred to
 * directly.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.SchemaSupport;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class SchemaGenerator {

  private static final String SERDE = "global.NSmithy.Core.Serde.";
  private static final String SCHEMAS = SERDE + "Schemas";
  private static final String GENERIC = "global.System.Collections.Generic.";

  private final FSharpContext context;
  private final Model model;
  private final FSharpSymbolProvider symbols;

  SchemaGenerator(FSharpContext context) {
    this.context = context;
    this.model = context.model();
    this.symbols = context.symbolProvider();
  }

  void stringEnum(EnumShape shape) {
    String type = symbols.typeOf(shape);
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add("values = " + FSharpLiterals.strings(SchemaSupport.stringEnumValues(shape)));
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    List<String> internal = SchemaSupport.internalStringEnumValues(shape);
    if (!internal.isEmpty()) {
      arguments.add("internalValues = " + FSharpLiterals.strings(internal));
    }
    writeHolder(
        context.files().enumSchemas(shape),
        shape,
        SERDE + "Schema<" + type + ">",
        SCHEMAS + ".StringEnum<" + type + ">(" + String.join(", ", arguments) + ")");
  }

  void intEnum(IntEnumShape shape) {
    String type = symbols.typeOf(shape);
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(
        "values = "
            + SchemaSupport.intEnumValues(shape).stream()
                .map(String::valueOf)
                .collect(Collectors.joining("; ", "[| ", " |]")));
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    writeHolder(
        context.files().enumSchemas(shape),
        shape,
        SERDE + "Schema<" + type + ">",
        SCHEMAS + ".IntEnum<" + type + ">(" + String.join(", ", arguments) + ")");
  }

  /** A structure or an error. */
  void structure(StructureShape shape) {
    FSharpWriter writer = context.files().schemas(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    String type = symbols.typeOf(shape);
    String builder = name + "Builder";
    String impl = name + "SchemaImpl";
    List<MemberShape> members = List.copyOf(shape.members());
    boolean isError = shape.hasTrait(ErrorTrait.class);
    Optional<MemberShape> message =
        isError ? ShapeSupport.errorMessageMember(model, shape) : Optional.empty();

    Map<String, String> properties = new LinkedHashMap<>();
    for (MemberShape member : members) {
      properties.put(property(member, isError, message), FSharpMembers.valueType(context, member));
    }
    List<String> accessors = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String target = impl + ".Target" + i;
      List<String> arguments = new ArrayList<>();
      arguments.add(FSharpNaming.stringLiteral(member.getMemberName()));
      arguments.add(target);
      if (ShapeSupport.isRequired(member)) {
        arguments.add("isRequired = true");
      }
      namedTraits(arguments, "traits", memberTraits(member));
      String memberSchema = SERDE + "MemberSchema(" + String.join(", ", arguments) + ")";
      String property = property(member, isError, message);
      String valueType = FSharpMembers.valueType(context, member);
      String get = "(fun (value: " + type + ") -> value." + property + ")";
      if (FSharpMembers.isOptional(member)) {
        accessors.add(
            String.format(
                "%sStructMember.Create<%s, %s, %s option>(%s, %s, %s, (fun (builder: %s) value ->"
                    + " builder.%s <- global.Microsoft.FSharp.Core.ValueOption.ofOption value))",
                SERDE,
                type,
                builder,
                valueType,
                memberSchema,
                optional(valueType, target),
                get,
                builder,
                property));
      } else {
        accessors.add(
            String.format(
                "%sStructMember.Create<%s, %s, %s>(%s, %s, %s, (fun (builder: %s) value ->"
                    + " builder.%s <- ValueSome value))",
                SERDE, type, builder, valueType, memberSchema, target, get, builder, property));
      }
    }
    Map<String, Object> templateContext = new HashMap<>();
    templateContext.put("builder", builder);
    templateContext.put("properties", properties);
    templateContext.put("impl", impl);
    templateContext.put("type", type);
    templateContext.put("shapeId", FSharpLiterals.shapeId(shape.getId()));
    templateContext.put(
        "accessors", array(accessors, SERDE + "StructMember<" + type + ", " + builder + ">"));
    templateContext.put("traits", traits(shape));
    templateContext.put("targets", targets(members));
    templateContext.put("build", build(shape, type, members, message, isError));
    writer.writeTemplate(
        """
        ${^properties}
        type ${builder:L}() = class end
        ${/properties}
        ${?properties}
        type ${builder:L}() =
            ${#properties}
            member val ${key:L}: ${value:L} voption = ValueNone with get, set
            ${/properties}
        ${/properties}

        type internal ${impl:L}() =
            inherit global.NSmithy.Core.Serde.AccessorStructSchema<${type:L}, ${builder:L}>(
                ${shapeId:L},
                ${accessors:L|}${?traits},
                traits = ${traits:L}${/traits})
            ${#targets}
            ${value:L}
            ${/targets}
            override _.CreateTypedBuilder() = ${builder:L}()
            ${build:L|}
        """,
        templateContext);

    writeHolder(writer, shape, SERDE + "StructSchema<" + type + ", " + builder + ">", impl + "()");
  }

  void union(UnionShape shape) {
    FSharpWriter writer = context.files().schemas(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    String type = symbols.typeOf(shape);
    String impl = name + "SchemaImpl";
    List<MemberShape> members = List.copyOf(shape.members());
    String noCase =
        "invalidOp " + FSharpNaming.stringLiteral("No union case matched '" + name + "'.");

    List<String> cases = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String target = impl + ".Target" + i;
      List<String> arguments = new ArrayList<>();
      arguments.add(FSharpNaming.stringLiteral(member.getMemberName()));
      arguments.add(target);
      namedTraits(arguments, "traits", memberTraits(member));
      String memberSchema = SERDE + "MemberSchema(" + String.join(", ", arguments) + ")";
      String caseName = type + "." + FSharpNaming.caseName(member);
      String valueType = FSharpMembers.valueType(context, member);
      String get;
      String create;
      if (ShapeSupport.isUnit(member.getTarget())) {
        get = "(fun (_: " + type + ") -> global.NSmithy.Core.SmithyUnit.Value)";
        create = "(fun (_: " + valueType + ") -> " + caseName + ")";
      } else {
        get =
            "(fun (value: "
                + type
                + ") -> match value with "
                + caseName
                + " value -> value | _ -> "
                + noCase
                + ")";
        create = "(fun (value: " + valueType + ") -> " + caseName + "(value))";
      }
      cases.add(
          String.format(
              "%sUnionCase.Create<%s, %s>(%s, %s, %s, %s)",
              SERDE, type, valueType, memberSchema, target, get, create));
    }
    List<String> caseOf = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      caseOf.add(
          "| "
              + type
              + "."
              + FSharpNaming.caseName(member)
              + (ShapeSupport.isUnit(member.getTarget()) ? "" : " _")
              + " -> "
              + i);
    }
    Map<String, Object> templateContext = new HashMap<>();
    templateContext.put("impl", impl);
    templateContext.put("type", type);
    templateContext.put("shapeId", FSharpLiterals.shapeId(shape.getId()));
    templateContext.put("cases", array(cases, SERDE + "UnionCase<" + type + ">"));
    templateContext.put("traits", traits(shape));
    templateContext.put("targets", targets(members));
    templateContext.put("caseOf", caseOf);
    templateContext.put("noCase", noCase);
    writer.writeTemplate(
        """
        type internal ${impl:L}() =
            inherit global.NSmithy.Core.Serde.AccessorUnionSchema<${type:L}>(
                ${shapeId:L},
                ${cases:L|}${?traits},
                traits = ${traits:L}${/traits})
            ${#targets}
            ${value:L}
            ${/targets}
            override _.CaseOf(value) =
                match value with
                ${#caseOf}
                ${value:L}
                ${/caseOf}
                | ${type:L}.Unknown _ -> ${noCase:L}
        """,
        templateContext);

    writeHolder(writer, shape, SERDE + "UnionSchema<" + type + ">", impl + "()");
  }

  void list(ListShape shape) {
    FSharpWriter writer = context.files().schemas(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    String impl = name + "SchemaImpl";
    String type = symbols.typeOf(shape);
    MemberShape member = shape.getMember();
    boolean sparse = ShapeSupport.isSparse(shape);
    String element = elementType(member, sparse);
    String builder = GENERIC + "List<" + element + ">";

    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(
        "global.NSmithy.Core.ShapeKind." + (shape.getType() == ShapeType.SET ? "Set" : "List"));
    arguments.add(impl + ".Target0");
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    namedTraits(arguments, "elementTraits", memberTraits(member));
    writer.writeTemplate(
        """
        type internal ${impl:L}() =
            inherit global.NSmithy.Core.Serde.ElementListSchema<${type:L}, ${element:L}, ${builder:L}>(
                ${arguments:L|})
            static member val Target0: global.NSmithy.Core.Serde.Schema<${element:L}> = ${elementSchema:L}
            override _.GetElements(value) = value :> global.System.Collections.Generic.IEnumerable<${element:L}>
            override _.CreateTypedBuilder() = ${builder:L}()
            override _.Add(builder, value) = builder.Add(value)
            override _.Build(builder) = global.Microsoft.FSharp.Collections.List.ofSeq builder
        """,
        Map.of(
            "impl", impl,
            "type", type,
            "element", element,
            "builder", builder,
            "arguments", String.join(",\n", arguments),
            "elementSchema", elementSchema(member, sparse)));

    writeHolder(
        writer,
        shape,
        SERDE + "ListSchema<" + type + ", " + element + ", " + builder + ">",
        impl + "()");
  }

  void map(MapShape shape) {
    FSharpWriter writer = context.files().schemas(shape);
    String name = FSharpNaming.typeName(shape.getId().getName());
    String impl = name + "SchemaImpl";
    String type = symbols.typeOf(shape);
    MemberShape member = shape.getValue();
    boolean sparse = ShapeSupport.isSparse(shape);
    String value = elementType(member, sparse);
    String builder = GENERIC + "Dictionary<string, " + value + ">";
    String entry = GENERIC + "KeyValuePair<string, " + value + ">";

    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(impl + ".Target0");
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    namedTraits(arguments, "keyTraits", memberTraits(shape.getKey()));
    namedTraits(arguments, "valueTraits", memberTraits(member));
    arguments.add("key = " + schema(model.expectShape(shape.getKey().getTarget())));
    writer.writeTemplate(
        """
        type internal ${impl:L}() =
            inherit global.NSmithy.Core.Serde.EntryMapSchema<${type:L}, ${valueType:L}, ${builder:L}>(
                ${arguments:L|})
            static member val Target0: global.NSmithy.Core.Serde.Schema<${valueType:L}> = ${valueSchema:L}
            override _.GetEntries(value) = value :> global.System.Collections.Generic.IEnumerable<${entry:L}>
            override _.CreateTypedBuilder() = ${builder:L}(global.System.StringComparer.Ordinal)
            override _.Add(builder, key, value) = builder[key] <- value
            override _.Build(builder) =
                global.Microsoft.FSharp.Collections.Map.ofSeq (global.Microsoft.FSharp.Collections.Seq.map (fun (entry: ${entry:L}) -> entry.Key, entry.Value) builder)
        """,
        Map.of(
            "impl", impl,
            "type", type,
            "valueType", value,
            "builder", builder,
            "entry", entry,
            "arguments", String.join(",\n", arguments),
            "valueSchema", elementSchema(member, sparse)));

    writeHolder(
        writer,
        shape,
        SERDE + "MapSchema<" + type + ", " + value + ", " + builder + ">",
        impl + "()");
  }

  /**
   * An operation's schema: its input, output, and errors, each with the HTTP status code it is sent
   * with.
   */
  void operation(OperationShape shape) {
    String input = operationType(shape.getInputShape());
    String output = operationType(shape.getOutputShape());
    List<String> errors = errorSchemas(shape.getErrors());
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(operationSchema(shape.getInputShape()));
    arguments.add(operationSchema(shape.getOutputShape()));
    arguments.add(array(errors, SERDE + "IOperationErrorSchema"));
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    if (ShapeSupport.isStreamingShape(model, shape.getInputShape())
        || ShapeSupport.isStreamingShape(model, shape.getOutputShape())) {
      arguments.add("isStreaming = true");
    }
    context
        .files()
        .operationSchemas(shape)
        .writeTemplate(
            """
            [<AbstractClass; Sealed>]
            type ${name:L} =
                static member val Schema: global.NSmithy.Core.Serde.OperationSchema<${input:L}, ${output:L}> =
                    global.NSmithy.Core.Serde.Schemas.Operation<${input:L}, ${output:L}>(
                        ${arguments:L|})
            """,
            Map.of(
                "name",
                symbols.schemaSymbol(shape).getName(),
                "input",
                input,
                "output",
                output,
                "arguments",
                String.join(",\n", arguments)));
  }

  void service(ServiceShape shape) {
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(FSharpNaming.stringLiteral(shape.getVersion()));
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    // The errors every operation of the service can raise. Operation schemas list only their own,
    // and protocols combine the two (ServiceSchema.ErrorsOf).
    if (!shape.getErrors().isEmpty()) {
      arguments.add(
          "errors = " + array(errorSchemas(shape.getErrors()), SERDE + "IOperationErrorSchema"));
    }
    writeHolder(
        context.files().schemas(shape),
        shape,
        SERDE + "ServiceSchema",
        SCHEMAS + ".Service(" + String.join(", ", arguments) + ")");
  }

  /**
   * The error schemas of {@code errorIds}, sorted by shape id, as an operation schema lists them.
   */
  private List<String> errorSchemas(List<ShapeId> errorIds) {
    List<String> errors = new ArrayList<>();
    for (ShapeId errorId : errorIds.stream().sorted().toList()) {
      StructureShape error = model.expectShape(errorId, StructureShape.class);
      errors.add(
          SCHEMAS
              + ".OperationError<"
              + symbols.typeOf(error)
              + ">("
              + FSharpLiterals.shapeId(errorId)
              + ", "
              + lazy(error)
              + ", "
              + SchemaSupport.errorStatusCode(error)
              + ") :> "
              + SERDE
              + "IOperationErrorSchema");
    }
    return errors;
  }

  /** The type an operation's input or output is: its structure, or {@code SmithyUnit}. */
  String operationType(ShapeId shape) {
    return ShapeSupport.isUnit(shape)
        ? "global.NSmithy.Core.SmithyUnit"
        : symbols.typeOf(model.expectShape(shape));
  }

  private String operationSchema(ShapeId shape) {
    return ShapeSupport.isUnit(shape) ? SCHEMAS + ".Unit" : lazy(model.expectShape(shape));
  }

  private void writeHolder(FSharpWriter writer, Shape shape, String schemaType, String value) {
    writer.writeTemplate(
        """
        [<AbstractClass; Sealed>]
        type ${name:L} =
            static member val Schema: ${schemaType:L} = ${value:L}
        """,
        Map.of(
            "name", symbols.schemaSymbol(shape).getName(),
            "schemaType", schemaType,
            "value", value));
  }

  /** The static values that hold each member's target schema, by the member's index. */
  private List<String> targets(List<MemberShape> members) {
    List<String> targets = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      targets.add(
          FSharpWriter.render(
              "static member val Target${index:L}: global.NSmithy.Core.Serde.Schema<${type:L}> ="
                  + " ${target:L}",
              Map.of(
                  "index", i,
                  "type", FSharpMembers.valueType(context, member),
                  "target", target(member))));
    }
    return targets;
  }

  /** The shape's own traits, or an empty string, which a template's {@code ${?traits}} skips. */
  private static String traits(Shape shape) {
    String traits = FSharpLiterals.traits(shape.getAllTraits().values());
    return traits == null ? "" : traits;
  }

  /** The schema class's Build override, which turns a filled-in builder into the value. */
  private String build(
      StructureShape shape,
      String type,
      List<MemberShape> members,
      Optional<MemberShape> message,
      boolean isError) {
    if (members.isEmpty()) {
      return FSharpWriter.render("override _.Build(_builder) = ${type:L}()", Map.of("type", type));
    }
    if (isError) {
      List<String> arguments = new ArrayList<>();
      for (MemberShape member : TypeGenerator.constructorOrder(shape)) {
        String parameter = FSharpNaming.parameterName(property(member, isError, message));
        String builderValue = "builder." + property(member, isError, message);
        if (FSharpMembers.isOptional(member) || FSharpMembers.usesDefault(member)) {
          arguments.add(
              "?"
                  + parameter
                  + " = global.Microsoft.FSharp.Core.ValueOption.toOption "
                  + builderValue);
        } else {
          arguments.add(parameter + " = " + required(member, builderValue));
        }
      }
      return FSharpWriter.render(
          """
          override _.Build(builder) =
              ${type:L}(
                  ${arguments:L|})\
          """,
          Map.of("type", type, "arguments", String.join(",\n", arguments)));
    }
    Map<String, String> fields = new LinkedHashMap<>();
    for (MemberShape member : members) {
      String builderValue = "builder." + FSharpNaming.fieldName(member);
      String value;
      if (FSharpMembers.isOptional(member)) {
        value = "global.Microsoft.FSharp.Core.ValueOption.toOption " + builderValue;
      } else if (FSharpMembers.usesDefault(member)) {
        value =
            "(match "
                + builderValue
                + " with ValueSome value -> value | ValueNone -> "
                + FSharpLiterals.defaultValue(context, member)
                + ")";
      } else {
        value = required(member, builderValue);
      }
      fields.put(FSharpNaming.fieldName(member), value);
    }
    return FSharpWriter.render(
        """
        override _.Build(builder) : ${type:L} =
            {
                ${#fields}
                ${key:L} = ${value:L}
                ${/fields}
            }\
        """,
        Map.of("type", type, "fields", fields));
  }

  /**
   * A required member's value. A missing value-typed member reads as its type's default, as a
   * format that leaves out zero values (protobuf) sends it; any other missing member is an error.
   */
  private String required(MemberShape member, String builderValue) {
    String missing =
        FSharpMembers.isValueType(model, member)
            ? "global.Microsoft.FSharp.Core.Operators.Unchecked.defaultof<_>"
            : "raise (global.NSmithy.Core.Serde.MissingRequiredMemberException("
                + FSharpNaming.stringLiteral(member.getMemberName())
                + "))";
    return "(match "
        + builderValue
        + " with ValueSome value -> value | ValueNone -> "
        + missing
        + ")";
  }

  /** A shape's schema, referenced from code that runs once every schema is initialized. */
  private String schema(Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return SCHEMAS + "." + SchemaSupport.preludeShapeSchemaName(shape);
    }
    if (shape.getType() == ShapeType.TIMESTAMP) {
      return SchemaSupport.timestampFormat(shape)
          .map(format ->
              SCHEMAS + ".TimestampWithTraits([| " + FSharpLiterals.trait(format) + " |])")
          .orElse(SCHEMAS + ".Timestamp");
    }
    if (shape.getType() != ShapeType.ENUM) {
      Optional<String> prelude = SchemaSupport.preludeSchemaName(shape.getType());
      if (prelude.isPresent()) {
        return SCHEMAS + "." + prelude.get();
      }
    }
    return FSharpSymbolProvider.render(symbols.schemaSymbol(shape)) + ".Schema";
  }

  /**
   * A member's target schema as a static value of its container's schema class. One that may be
   * part of a cycle is resolved lazily.
   */
  private String target(MemberShape member) {
    Shape shape = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return SCHEMAS + ".StreamingBlob";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return SCHEMAS + ".EventStream(" + lazy(shape) + ")";
    }
    return lazy(shape);
  }

  private String lazy(Shape shape) {
    if (!SchemaSupport.isCycleCapable(shape.getType())
        || ShapeSupport.isUnit(shape.getId())
        || ShapeSupport.isValidationException(shape.getId())) {
      return schema(shape);
    }
    String type = symbols.typeOf(shape);
    return SCHEMAS
        + ".Lazy<"
        + type
        + ">(fun () -> "
        + schema(shape)
        + " :> "
        + SERDE
        + "Schema<"
        + type
        + ">)";
  }

  private String elementSchema(MemberShape member, boolean sparse) {
    String target = lazy(model.expectShape(member.getTarget()));
    if (!sparse) {
      return target;
    }
    return optional(symbols.typeOf(model.expectShape(member.getTarget())), target);
  }

  /** The schema of an {@code option} whose present value goes through {@code target}. */
  private static String optional(String type, String target) {
    return String.format(
        "%s.Optional<%s option, %s>(%s, (fun value -> value.IsSome), (fun value -> value.Value),"
            + " (fun value -> Some value), None)",
        SCHEMAS, type, type, target);
  }

  private String elementType(MemberShape member, boolean sparse) {
    String type = symbols.typeOf(model.expectShape(member.getTarget()));
    return sparse ? type + " option" : type;
  }

  private String memberTraits(MemberShape member) {
    List<software.amazon.smithy.model.traits.Trait> traits =
        SchemaSupport.memberTraits(model, member);
    return FSharpLiterals.traits(traits);
  }

  /** The builder property, and record field or error property, a member is held in. */
  private static String property(
      MemberShape member, boolean isError, Optional<MemberShape> message) {
    return isError
        ? TypeGenerator.errorPropertyName(member, message)
        : FSharpNaming.fieldName(member);
  }

  private static void namedTraits(List<String> arguments, String name, String traits) {
    if (traits != null) {
      arguments.add(name + " = " + traits);
    }
  }

  private static String array(List<String> elements, String elementType) {
    if (elements.isEmpty()) {
      return "([||]: " + elementType + "[])";
    }
    return "[|\n    " + String.join("\n    ", elements) + "\n|]";
  }
}

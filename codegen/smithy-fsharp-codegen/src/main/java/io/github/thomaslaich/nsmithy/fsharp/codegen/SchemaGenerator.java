/*
 * Declares the runtime schema of each shape in the shape's Schemas namespace.
 *
 * A structure, union, list, or map gets a class deriving from the runtime's StructSchema,
 * UnionSchema, ListSchema, or MapSchema, which reads and writes the F# type directly, and a holder
 * type whose Schema property is the one instance:
 *
 *   PersonBuilder      what a deserializer fills in, one value option per member
 *   PersonSchemaImpl   the schema class (internal)
 *   PersonSchema       the holder: PersonSchema.Schema
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
import java.util.List;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
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
  private static final String OPTION_ADAPTER = "SchemaOptionAdapter";

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

    // The builder.
    if (members.isEmpty()) {
      writer.write("type $L() = class end\n", builder);
    } else {
      writer.write("type $L() =", builder).indent();
      for (MemberShape member : members) {
        writer.write(
            "member val $L: $L voption = ValueNone with get, set",
            property(member, isError, message),
            FSharpMembers.valueType(context, member));
      }
      writer.dedent().write("");
    }

    // The schema class.
    List<String> declarations = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      List<String> arguments = new ArrayList<>();
      arguments.add(FSharpNaming.stringLiteral(member.getMemberName()));
      arguments.add(impl + ".Target" + i);
      if (ShapeSupport.isRequired(member)) {
        arguments.add("isRequired = true");
      }
      namedTraits(arguments, "traits", memberTraits(member));
      declarations.add(SERDE + "MemberSchema(" + String.join(", ", arguments) + ")");
    }
    writer.write("type internal $L() =", impl).indent();
    writer.write("inherit $LStructSchema<$L, $L>(", SERDE, type, builder).indent();
    writer.write("$L,", FSharpLiterals.shapeId(shape.getId()));
    String traits = FSharpLiterals.traits(shape.getAllTraits().values());
    writer.write(
        "$L$L)",
        array(declarations, SERDE + "MemberSchema"),
        traits == null ? "" : ",\ntraits = " + traits);
    writer.dedent();
    writeTargets(writer, members);
    writer.write("override _.CreateTypedBuilder() = $L()", builder);
    writeBuild(writer, shape, type, members, message, isError);
    writeSerializeMembers(writer, impl, members, isError, message);
    writeDeserializeMember(writer, impl, members, isError, message);
    writer.dedent().write("");

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

    List<String> declarations = new ArrayList<>();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      List<String> arguments = new ArrayList<>();
      arguments.add(FSharpNaming.stringLiteral(member.getMemberName()));
      arguments.add(impl + ".Target" + i);
      namedTraits(arguments, "traits", memberTraits(member));
      declarations.add(SERDE + "MemberSchema(" + String.join(", ", arguments) + ")");
    }
    writer.write("type internal $L() =", impl).indent();
    writer.write("inherit $LUnionSchema<$L>(", SERDE, type).indent();
    writer.write("$L,", FSharpLiterals.shapeId(shape.getId()));
    String traits = FSharpLiterals.traits(shape.getAllTraits().values());
    writer.write(
        "$L$L)",
        array(declarations, SERDE + "MemberSchema"),
        traits == null ? "" : ",\ntraits = " + traits);
    writer.dedent();
    writeTargets(writer, members);

    writer.write("override _.CaseOf(value) =").indent().write("match value with");
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      writer.write(
          "| $L.$L$L -> $L",
          type,
          FSharpNaming.caseName(member),
          ShapeSupport.isUnit(member.getTarget()) ? "" : " _",
          i);
    }
    writer.write("| $L.Unknown _ -> $L", type, noCase).dedent();

    writer
        .write("override _.SerializeCase(value, serializer) =")
        .indent()
        .write("match value with");
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String caseName = type + "." + FSharpNaming.caseName(member);
      if (ShapeSupport.isUnit(member.getTarget())) {
        writer.write("| $L ->", caseName).indent();
        writer.write(
            "$L.Target$L.Write($L, global.NSmithy.Core.SmithyUnit.Value, &serializer)", impl, i, i);
      } else {
        writer.write("| $L case$L ->", caseName, i).indent();
        writer.write("$L", write(member, String.valueOf(i), "case" + i, impl + ".Target" + i));
      }
      writer.dedent();
    }
    writer.write("| $L.Unknown _ -> $L", type, noCase).dedent();

    writer
        .write("override _.DeserializeCase(index, deserializer) =")
        .indent()
        .write("match index with");
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String caseName = type + "." + FSharpNaming.caseName(member);
      if (ShapeSupport.isUnit(member.getTarget())) {
        writer.write("| $L ->", i).indent();
        writer.write("$L.Target$L.Read(&deserializer) |> ignore", impl, i);
        writer.write("$L", caseName).dedent();
      } else {
        writer.write("| $L -> $L($L)", i, caseName, read(member, impl + ".Target" + i));
      }
    }
    writer.write("| _ -> $L", noCase).dedent();
    writer.dedent().write("");

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

    writer.write("type internal $L() =", impl).indent();
    writer.write("inherit $LListSchema<$L, $L, $L>(", SERDE, type, element, builder).indent();
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(
        "global.NSmithy.Core.ShapeKind." + (shape.getType() == ShapeType.SET ? "Set" : "List"));
    arguments.add(impl + ".Target0");
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    namedTraits(arguments, "elementTraits", memberTraits(member));
    writer.write("$L)", String.join(",\n", arguments)).dedent();
    writer.write(
        "static member val Target0: $LSchema<$L> = $L",
        SERDE,
        element,
        elementSchema(shape, member, sparse));
    writer.write("override _.GetElements(value) = value :> $LIEnumerable<$L>", GENERIC, element);
    writer.write("override _.CreateTypedBuilder() = $L()", builder);
    writer.write("override _.Add(builder, value) = builder.Add(value)");
    writer.write(
        "override _.Build(builder) = global.Microsoft.FSharp.Collections.List.ofSeq builder");
    writer.write("override _.SerializeElements(value, serializer) =").indent();
    writer.write("for element in value do").indent();
    writer
        .write("$L", writeElement(member, sparse, "0", "element", impl + ".Target0"))
        .dedent()
        .dedent();
    writer.write("override _.DeserializeElement(builder, deserializer) =").indent();
    writer.write("$L", readElement(member, sparse, "builder.Add", impl + ".Target0")).dedent();
    writer.dedent().write("");

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

    writer.write("type internal $L() =", impl).indent();
    writer.write("inherit $LMapSchema<$L, $L, $L>(", SERDE, type, value, builder).indent();
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpLiterals.shapeId(shape.getId()));
    arguments.add(impl + ".Target0");
    namedTraits(arguments, "traits", FSharpLiterals.traits(shape.getAllTraits().values()));
    namedTraits(arguments, "keyTraits", memberTraits(shape.getKey()));
    namedTraits(arguments, "valueTraits", memberTraits(member));
    arguments.add("key = " + schema(model.expectShape(shape.getKey().getTarget())));
    writer.write("$L)", String.join(",\n", arguments)).dedent();
    writer.write(
        "static member val Target0: $LSchema<$L> = $L",
        SERDE,
        value,
        elementSchema(shape, member, sparse));
    writer.write("override _.GetEntries(value) = value :> $LIEnumerable<$L>", GENERIC, entry);
    writer.write(
        "override _.CreateTypedBuilder() = $L(global.System.StringComparer.Ordinal)", builder);
    writer.write("override _.Add(builder, key, value) = builder[key] <- value");
    writer.write("override _.Build(builder) =").indent();
    writer.write(
        "global.Microsoft.FSharp.Collections.Map.ofSeq (global.Microsoft.FSharp.Collections.Seq.map"
            + " (fun (entry: $L) -> entry.Key, entry.Value) builder)",
        entry);
    writer.dedent();
    writer.write("override _.SerializeEntries(value, serializer) =").indent();
    writer.write("for entry in value do").indent();
    writer.write("serializer.WriteString(0, entry.Key)");
    writer
        .write("$L", writeElement(member, sparse, "1", "entry.Value", impl + ".Target0"))
        .dedent()
        .dedent();
    writer.write("override _.DeserializeEntry(builder, key, deserializer) =").indent();
    writer.write("$L", readElement(member, sparse, "builder[key] <- ", impl + ".Target0")).dedent();
    writer.dedent().write("");

    writeHolder(
        writer,
        shape,
        SERDE + "MapSchema<" + type + ", " + value + ", " + builder + ">",
        impl + "()");
  }

  /** The schema adapter for the {@code option} elements of a sparse list or map. */
  void writeOptionAdapter(FSharpWriter writer) {
    writer.write(
        """
        type internal $1L<'T>(target: $2LSchema<'T>) =
            inherit $2LSchema<'T option>()
            override _.Id = target.Id
            override _.Kind = target.Kind
            override _.Traits = target.Traits
            override _.Write(index, value, serializer) =
                match value with
                | Some value -> target.Write(index, value, &serializer)
                | None -> serializer.WriteNull(index)
            override _.Read(deserializer) =
                if deserializer.TryReadNull() then None else Some(target.Read(&deserializer))
            interface $2LINullableSchema with
                member _.Target = target :> $2LSchema
        """,
        OPTION_ADAPTER,
        SERDE);
  }

  private void writeHolder(FSharpWriter writer, Shape shape, String schemaType, String value) {
    writer.write(
        """
        [<AbstractClass; Sealed>]
        type $L =
            static member val Schema: $L = $L
        """,
        symbols.schemaSymbol(shape).getName(),
        schemaType,
        value);
  }

  private void writeTargets(FSharpWriter writer, List<MemberShape> members) {
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      writer.write(
          "static member val Target$L: $LSchema<$L> = $L",
          i,
          SERDE,
          FSharpMembers.valueType(context, member),
          target(member));
    }
  }

  private void writeBuild(
      FSharpWriter writer,
      StructureShape shape,
      String type,
      List<MemberShape> members,
      Optional<MemberShape> message,
      boolean isError) {
    if (members.isEmpty()) {
      writer.write("override _.Build(_builder) = $L()", type);
      return;
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
      writer.write("override _.Build(builder) =").indent();
      writer.write("$L(", type).indent();
      writer.write("$L)", String.join(",\n", arguments)).dedent().dedent();
      return;
    }
    writer.write("override _.Build(builder) : $L =", type).indent();
    writer.write("{").indent();
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
      writer.write("$L = $L", FSharpNaming.fieldName(member), value);
    }
    writer.dedent().write("}").dedent();
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

  private void writeSerializeMembers(
      FSharpWriter writer,
      String impl,
      List<MemberShape> members,
      boolean isError,
      Optional<MemberShape> message) {
    if (members.isEmpty()) {
      writer.write("override _.SerializeMembers(_value, _serializer) = ()");
      return;
    }
    writer.write("override _.SerializeMembers(value, serializer) =").indent();
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String value = "value." + property(member, isError, message);
      String target = impl + ".Target" + i;
      if (FSharpMembers.isOptional(member)) {
        writer.write("match $L with", value);
        writer.write(
            "| Some member$L -> $L", i, write(member, String.valueOf(i), "member" + i, target));
        writer.write("| None -> serializer.WriteNull($L)", i);
      } else {
        writer.write("$L", write(member, String.valueOf(i), value, target));
      }
    }
    writer.dedent();
  }

  private void writeDeserializeMember(
      FSharpWriter writer,
      String impl,
      List<MemberShape> members,
      boolean isError,
      Optional<MemberShape> message) {
    if (members.isEmpty()) {
      writer.write("override _.DeserializeMember(_builder, _index, _deserializer) = ()");
      return;
    }
    writer.write("override _.DeserializeMember(builder, index, deserializer) =").indent();
    writer.write("match index with");
    for (int i = 0; i < members.size(); i++) {
      MemberShape member = members.get(i);
      String property = "builder." + property(member, isError, message);
      String read = read(member, impl + ".Target" + i);
      writer.write("| $L ->", i).indent();
      if (FSharpMembers.isValueType(model, member)) {
        writer.write(
            "$L <- if deserializer.TryReadNull() then ValueNone else ValueSome($L)",
            property,
            read);
      } else {
        writer.write("let value = $L", read);
        writer.write("$L <- if isNull (box value) then ValueNone else ValueSome value", property);
      }
      writer.dedent();
    }
    writer.write("| _ -> ()").dedent();
  }

  /** Writes a list element or map value, which is an {@code option} in a sparse collection. */
  private String writeElement(
      MemberShape member, boolean sparse, String index, String value, String target) {
    if (!sparse) {
      return write(member, index, value, target);
    }
    return "match "
        + value
        + " with\n| Some present -> "
        + write(member, index, "present", target)
        + "\n| None -> serializer.WriteNull("
        + index
        + ")";
  }

  /** Reads a list element or map value and hands it to {@code add}. */
  private String readElement(MemberShape member, boolean sparse, String add, String target) {
    String read = read(member, target);
    String assign = add.endsWith(" ") ? add : add + "(";
    String close = add.endsWith(" ") ? "" : ")";
    if (!sparse) {
      return assign + read + close;
    }
    if (FSharpMembers.isValueType(model, member)) {
      return assign + "(if deserializer.TryReadNull() then None else Some(" + read + "))" + close;
    }
    return "let element = "
        + read
        + "\n"
        + assign
        + "(if isNull (box element) then None else Some element)"
        + close;
  }

  /**
   * The call writing {@code value} as member {@code index}: the serializer's own call for the
   * member's kind, or the member's target schema for a kind it has none for.
   */
  private String write(MemberShape member, String index, String value, String target) {
    Shape shape = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "serializer.WriteStream(" + index + ", " + value + ")";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return "serializer.WriteEventStream(" + index + ", " + value + ", " + schema(shape) + ")";
    }
    if (ShapeSupport.isUnit(shape.getId()) || ShapeSupport.isValidationException(shape.getId())) {
      return target + ".Write(" + index + ", " + value + ", &serializer)";
    }
    String call =
        switch (shape.getType()) {
          case BOOLEAN -> "WriteBoolean";
          case BYTE -> "WriteByte";
          case SHORT -> "WriteShort";
          case INTEGER -> "WriteInteger";
          case LONG -> "WriteLong";
          case FLOAT -> "WriteFloat";
          case DOUBLE -> "WriteDouble";
          case BIG_INTEGER -> "WriteBigInteger";
          case BIG_DECIMAL -> "WriteBigDecimal";
          case STRING -> "WriteString";
          case BLOB -> "WriteBlob";
          case TIMESTAMP -> "WriteTimestamp";
          case DOCUMENT -> "WriteDocument";
          default -> null;
        };
    if (call != null) {
      return "serializer." + call + "(" + index + ", " + value + ")";
    }
    return switch (shape.getType()) {
      case ENUM -> "serializer.WriteStringEnum(" + index + ", " + value + ".Value)";
      case INT_ENUM -> "serializer.WriteIntEnum(" + index + ", int " + value + ")";
      case STRUCTURE ->
          "serializer.WriteStruct(" + index + ", " + value + ", " + schema(shape) + ")";
      case UNION -> "serializer.WriteUnion(" + index + ", " + value + ", " + schema(shape) + ")";
      case LIST, SET -> "serializer.WriteList(" + index + ", " + value + ", " + schema(shape) + ")";
      case MAP -> "serializer.WriteMap(" + index + ", " + value + ", " + schema(shape) + ")";
      default -> target + ".Write(" + index + ", " + value + ", &serializer)";
    };
  }

  /** The expression reading a member's value, calling the deserializer for the member's kind. */
  private String read(MemberShape member, String target) {
    Shape shape = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "deserializer.ReadStream()";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return "deserializer.ReadEventStream(" + schema(shape) + ")";
    }
    if (ShapeSupport.isUnit(shape.getId()) || ShapeSupport.isValidationException(shape.getId())) {
      return target + ".Read(&deserializer)";
    }
    return switch (shape.getType()) {
      case BOOLEAN -> "deserializer.ReadBoolean()";
      case BYTE -> "deserializer.ReadByte()";
      case SHORT -> "deserializer.ReadShort()";
      case INTEGER -> "deserializer.ReadInteger()";
      case LONG -> "deserializer.ReadLong()";
      case FLOAT -> "deserializer.ReadFloat()";
      case DOUBLE -> "deserializer.ReadDouble()";
      case BIG_INTEGER -> "deserializer.ReadBigInteger()";
      case BIG_DECIMAL -> "deserializer.ReadBigDecimal()";
      case STRING -> "deserializer.ReadString()";
      case BLOB -> "deserializer.ReadBlob()";
      case TIMESTAMP -> "deserializer.ReadTimestamp()";
      case DOCUMENT -> "deserializer.ReadDocument()";
      // A format whose enums are ordinals (protobuf) reads an ordinal the model does not define as
      // null, which becomes the Unknown case.
      case ENUM -> symbols.typeOf(shape) + ".FromValue(deserializer.ReadStringEnum())";
      case INT_ENUM ->
          "global.Microsoft.FSharp.Core.LanguagePrimitives.EnumOfValue<int, "
              + symbols.typeOf(shape)
              + ">(deserializer.ReadIntEnum())";
      case STRUCTURE -> "deserializer.ReadStruct(" + schema(shape) + ")";
      case UNION -> "deserializer.ReadUnion(" + schema(shape) + ")";
      case LIST, SET -> "deserializer.ReadList(" + schema(shape) + ")";
      case MAP -> "deserializer.ReadMap(" + schema(shape) + ")";
      default -> target + ".Read(&deserializer)";
    };
  }

  /** A shape's schema, referenced from code that runs once every schema is initialized. */
  private String schema(Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return SCHEMAS + "." + SchemaSupport.preludeShapeSchemaName(shape);
    }
    if (shape.getType() == ShapeType.TIMESTAMP) {
      return SchemaSupport.timestampFormat(shape)
          .map(
              format ->
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

  private String elementSchema(Shape collection, MemberShape member, boolean sparse) {
    String target = lazy(model.expectShape(member.getTarget()));
    if (!sparse) {
      return target;
    }
    context.files().useOptionAdapter(collection);
    return OPTION_ADAPTER
        + "<"
        + symbols.typeOf(model.expectShape(member.getTarget()))
        + ">("
        + target
        + ")";
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

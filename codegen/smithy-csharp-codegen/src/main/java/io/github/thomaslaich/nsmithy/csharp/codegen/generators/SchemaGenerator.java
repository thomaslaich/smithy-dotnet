package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.math.BigDecimal;
import java.time.Instant;
import java.time.OffsetDateTime;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.IntEnumShape;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.DefaultTrait;
import software.amazon.smithy.model.traits.EnumValueTrait;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.InternalTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class SchemaGenerator {

  public static String shapeSchemaAccessor(
      CSharpWriter writer, GenerationContext context, Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return switch (shape.getId().getName()) {
        case "Boolean" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Boolean");
        case "Byte" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Byte");
        case "Short" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Short");
        case "Integer" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Integer");
        case "Long" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Long");
        case "Float" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Float");
        case "Double" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Double");
        case "BigInteger" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigInteger");
        case "BigDecimal" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigDecimal");
        case "String" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".String");
        case "Blob" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Blob");
        case "Timestamp" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp");
        case "Document" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Document");
        case "Unit" -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Unit");
        default ->
            throw new CodegenException(
                "Unsupported Smithy prelude schema shape "
                    + shape.getId()
                    + " ("
                    + shape.getType()
                    + "). Supported prelude schema shapes: "
                    + supportedPreludeSchemaShapeNames()
                    + ".");
      };
    }

    if (shape.getType() == ShapeType.TIMESTAMP) {
      // Carry @timestampFormat into the schema so codecs resolve the wire format from it
      // (covers struct members, list elements, and map values uniformly).
      List<Trait> tsTraits =
          shape.getAllTraits().values().stream()
              .filter(t -> t.toShapeId().toString().equals("smithy.api#timestampFormat"))
              .collect(Collectors.toList());
      return tsTraits.isEmpty()
          ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp")
          : (writer.typeName(RuntimeTypes.SCHEMAS) + ".TimestampWithTraits(")
              + traitsExpr(writer, tsTraits)
              + ")";
    }

    String preludeSchema =
        shape.getType() == ShapeType.ENUM
            ? null
            : primitiveTypeToPreludeSchema(writer, shape.getType());
    if (preludeSchema != null) {
      return preludeSchema;
    }

    String accessor = schemaClassName(writer, context, shape) + ".Schema";

    // Aggregate shapes can participate in recursive graphs (a shape referencing itself
    // directly or through a cycle). A direct static reference would observe null while the
    // referenced schema's static initializer is still running, so defer it lazily. The
    // null-forgiving '!' suppresses the nullable-flow warning for self-references where the
    // property is not yet definitely assigned at the point the lambda is created.
    return isCycleCapable(shape.getType())
        ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".Lazy(() => ") + accessor + "!)"
        : accessor;
  }

  public static String schemaClassName(
      CSharpWriter writer, GenerationContext context, Shape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape), "Schema");
  }

  public static String operationSchemaAccessor(
      CSharpWriter writer, GenerationContext context, OperationShape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape), "Schema") + ".Schema";
  }

  public static String serviceSchemaAccessor(
      CSharpWriter writer,
      GenerationContext context,
      software.amazon.smithy.model.shapes.ServiceShape service) {
    return writer.typeName(context.symbolProvider().toSymbol(service), "Schema") + ".Schema";
  }

  public static String operationShapeType(
      CSharpWriter writer, GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return writer.typeName(RuntimeTypes.SMITHY_UNIT);
    return writer.typeName(context.symbolProvider().toSymbol(context.model().expectShape(id)));
  }

  public static String operationShapeSchema(
      CSharpWriter writer, GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return (writer.typeName(RuntimeTypes.SCHEMAS) + ".Unit");
    return shapeSchemaAccessor(writer, context, context.model().expectShape(id));
  }

  public static String shapeIdExpr(CSharpWriter writer, ShapeId id) {
    return (writer.typeName(RuntimeTypes.SHAPE_ID) + ".Parse(")
        + CSharpNaming.formatString(id.toString())
        + ")";
  }

  public static String traitExpr(CSharpWriter writer, Trait trait) {
    String idExpr = shapeIdExpr(writer, trait.toShapeId());
    Node node = trait.toNode();
    if (node.isNullNode()) {
      return ("new " + writer.typeName(RuntimeTypes.TRAIT) + "(") + idExpr + ")";
    }

    return ("new " + writer.typeName(RuntimeTypes.TRAIT) + "(")
        + idExpr
        + ", "
        + documentExpr(writer, node)
        + ")";
  }

  public static String traitsExpr(CSharpWriter writer, Collection<? extends Trait> traits) {
    if (traits.isEmpty()) {
      return "null";
    }

    List<Trait> sorted = new ArrayList<>(traits);
    sorted.sort(java.util.Comparator.comparing(t -> t.toShapeId().toString()));
    return "["
        + sorted.stream().map(value -> traitExpr(writer, value)).collect(Collectors.joining(", "))
        + "]";
  }

  /** The name of the schema class generated inside each shape's {@code ...Schema} class. */
  private static final String GENERATED_SCHEMA = "GeneratedSchema";

  /**
   * Writes a structure's schema as a class deriving from {@code StructSchema}: the members are
   * declared once, and the structure's properties are read and written directly, one call per
   * member, with no per-member delegates.
   */
  public static void writeStructureSchema(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    writer.pushState();
    try {
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("generatedSchema", GENERATED_SCHEMA);
      writer.putContext("type", context.symbolProvider().toSymbol(shape));
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("structSchema", RuntimeTypes.STRUCT_SCHEMA);
      writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
      writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
      writer.putContext(
          "constructorArguments", constructorArguments(writer, context, shape, members));
      writer.putContext(
          "builderProperties",
          writer.consumer(w -> writeStructureBuilderProperties(w, context, members)));
      writer.putContext(
          "memberDeclarations",
          writer.consumer(w -> writeStructureMemberDeclarations(w, context, members)));
      writer.putContext(
          "targets", writer.consumer(w -> writeStructureTargets(w, context, members)));
      boolean isError = shape.hasTrait(ErrorTrait.class);
      writer.putContext("builderDefaults", builderDefaults(writer, context, members));
      writer.putContext(
          "writes",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  MemberShape member = members.get(index);
                  writeValue(
                      w,
                      context,
                      member,
                      String.valueOf(index),
                      "value." + CSharpNaming.propertyName(member.getMemberName()),
                      "value" + index,
                      propertyMayBeNull(context, member, isError),
                      defaultLiteral(w, context, member),
                      "Target" + index);
                }
              }));
      // A structure with no members has nothing to read, and an empty switch does not compile.
      writer.putContext(
          "reads",
          writer.consumer(
              w -> {
                if (members.isEmpty()) {
                  return;
                }
                w.openBlock(
                    "switch (index)\n{",
                    "}",
                    () -> {
                      for (int index = 0; index < members.size(); index++) {
                        MemberShape member = members.get(index);
                        w.write(
                            """
                            case $L:
                                builder.$L = $L;
                                break;\
                            """,
                            index,
                            CSharpNaming.propertyName(member.getMemberName()),
                            readValue(
                                w,
                                context,
                                member,
                                "value" + index,
                                !ShapeSupport.isReferenceType(context.model(), member),
                                "Target" + index));
                      }
                    });
              }));
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public sealed class Builder
              {
                  ${builderProperties:C|}
              }

              public static ${structSchema:T}<${type:T}, Builder> Schema { get; } =
                  new ${generatedSchema:L}();

              private sealed class ${generatedSchema:L}()
                  : ${structSchema:T}<${type:T}, Builder>(
                      ${shapeId:L},
                      [
                          ${memberDeclarations:C|}
                      ],
                      ${traits:L})
              {
                  ${targets:C|}

                  public override Builder CreateTypedBuilder() => new()${builderDefaults:L};

                  public override ${type:T} Build(Builder builder) =>
                      new ${type:T}(${constructorArguments:L});

                  public override void SerializeMembers<TSerializer>(
                      ${type:T} value,
                      ref TSerializer serializer)
                  {
                      ${writes:C|}
                  }

                  public override void DeserializeMember<TDeserializer>(
                      Builder builder,
                      int index,
                      ref TDeserializer deserializer)
                  {
                      ${reads:C|}
                  }
              }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * Writes a list's schema as a class deriving from {@code ListSchema}, which reads and writes each
   * element directly.
   */
  public static void writeListSchema(
      CSharpWriter writer, GenerationContext context, ListShape shape) {
    SymbolProvider sp = context.symbolProvider();
    MemberShape member = shape.getMember();
    Shape memberTarget = context.model().expectShape(member.getTarget());
    String typeName = writer.typeName(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    boolean isReference = ShapeSupport.isReferenceType(context.model(), member);
    String memberType = writer.typeName(sp.toSymbol(memberTarget)) + (sparse ? "?" : "");
    String builderType = writer.typeName(RuntimeTypes.LIST) + "<" + memberType + ">";

    writer.pushState();
    try {
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("generatedSchema", GENERATED_SCHEMA);
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("listSchema", RuntimeTypes.LIST_SCHEMA);
      writer.putContext("shapeKind", RuntimeTypes.SHAPE_KIND);
      writer.putContext("enumerable", RuntimeTypes.I_ENUMERABLE);
      writer.putContext("type", typeName);
      writer.putContext("memberType", memberType);
      writer.putContext("builderType", builderType);
      writer.putContext("kind", shape.getType() == ShapeType.SET ? "Set" : "List");
      writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
      writer.putContext(
          "elementSchema", elementSchemaExpr(writer, context, member, memberTarget, sparse));
      writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
      writer.putContext("elementTraits", memberTraitsExpr(writer, context, member));
      writer.putContext(
          "write",
          writer.consumer(
              w ->
                  writeValue(
                      w,
                      context,
                      member,
                      "0",
                      "elements[index]",
                      "element",
                      isReference || sparse,
                      null,
                      "Target0")));
      writer.putContext(
          "read", readValue(writer, context, member, "element", sparse && !isReference, "Target0"));
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public static ${listSchema:T}<${type:L}, ${memberType:L}, ${builderType:L}> Schema { get; } =
                  new ${generatedSchema:L}();

              private sealed class ${generatedSchema:L}()
                  : ${listSchema:T}<${type:L}, ${memberType:L}, ${builderType:L}>(
                      ${shapeId:L},
                      ${shapeKind:T}.${kind:L},
                      Target0,
                      ${traits:L},
                      ${elementTraits:L})
              {
                  private static readonly ${schema:T}<${memberType:L}> Target0 = ${elementSchema:L};

                  public override ${enumerable:T}<${memberType:L}> GetElements(${type:L} value) =>
                      value.Values;

                  public override ${builderType:L} CreateTypedBuilder() => new();

                  public override void Add(${builderType:L} builder, ${memberType:L} value) =>
                      builder.Add(value);

                  public override ${type:L} Build(${builderType:L} builder) =>
                      ${type:L}.FromOwnedList(builder);

                  public override void SerializeElements<TSerializer>(
                      ${type:L} value,
                      ref TSerializer serializer)
                  {
                      var elements = value.Values;
                      for (var index = 0; index < elements.Count; index++)
                      {
                          ${write:C|}
                      }
                  }

                  public override void DeserializeElement<TDeserializer>(
                      ${builderType:L} builder,
                      ref TDeserializer deserializer) =>
                      builder.Add(${read:L});
              }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * Writes a map's schema as a class deriving from {@code MapSchema}, which reads and writes each
   * entry's value directly. A key is always written as a string, whatever shape it targets.
   */
  public static void writeMapSchema(
      CSharpWriter writer, GenerationContext context, MapShape shape) {
    SymbolProvider sp = context.symbolProvider();
    MemberShape member = shape.getValue();
    Shape valueTarget = context.model().expectShape(member.getTarget());
    String typeName = writer.typeName(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    boolean isReference = ShapeSupport.isReferenceType(context.model(), member);
    String valueType = writer.typeName(sp.toSymbol(valueTarget)) + (sparse ? "?" : "");
    String builderType = writer.typeName(RuntimeTypes.DICTIONARY) + "<string, " + valueType + ">";

    writer.pushState();
    try {
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("generatedSchema", GENERATED_SCHEMA);
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("mapSchema", RuntimeTypes.MAP_SCHEMA);
      writer.putContext("enumerable", RuntimeTypes.I_ENUMERABLE);
      writer.putContext("keyValuePair", RuntimeTypes.KEY_VALUE_PAIR);
      writer.putContext("stringComparer", RuntimeTypes.STRING_COMPARER);
      writer.putContext("type", typeName);
      writer.putContext("valueType", valueType);
      writer.putContext("builderType", builderType);
      writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
      writer.putContext(
          "valueSchema", elementSchemaExpr(writer, context, member, valueTarget, sparse));
      writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
      writer.putContext("keyTraits", memberTraitsExpr(writer, context, shape.getKey()));
      writer.putContext("valueTraits", memberTraitsExpr(writer, context, member));
      writer.putContext(
          "keySchema",
          shapeSchemaAccessor(
              writer, context, context.model().expectShape(shape.getKey().getTarget())));
      writer.putContext(
          "write",
          writer.consumer(
              w ->
                  writeValue(
                      w,
                      context,
                      member,
                      "1",
                      "entry.Value",
                      "entryValue",
                      isReference || sparse,
                      null,
                      "Target0")));
      writer.putContext(
          "read",
          readValue(writer, context, member, "entryValue", sparse && !isReference, "Target0"));
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public static ${mapSchema:T}<${type:L}, ${valueType:L}, ${builderType:L}> Schema { get; } =
                  new ${generatedSchema:L}();

              private sealed class ${generatedSchema:L}()
                  : ${mapSchema:T}<${type:L}, ${valueType:L}, ${builderType:L}>(
                      ${shapeId:L},
                      Target0,
                      ${traits:L},
                      ${keyTraits:L},
                      ${valueTraits:L},
                      ${keySchema:L})
              {
                  private static readonly ${schema:T}<${valueType:L}> Target0 = ${valueSchema:L};

                  public override ${enumerable:T}<${keyValuePair:T}<string, ${valueType:L}>> GetEntries(
                      ${type:L} value) => value.Values;

                  public override ${builderType:L} CreateTypedBuilder() =>
                      new(${stringComparer:T}.Ordinal);

                  public override void Add(${builderType:L} builder, string key, ${valueType:L} value) =>
                      builder[key] = value;

                  public override ${type:L} Build(${builderType:L} builder) =>
                      ${type:L}.FromOwnedDictionary(builder);

                  public override void SerializeEntries<TSerializer>(
                      ${type:L} value,
                      ref TSerializer serializer)
                  {
                      foreach (var entry in value.Values)
                      {
                          serializer.WriteString(0, entry.Key);
                          ${write:C|}
                      }
                  }

                  public override void DeserializeEntry<TDeserializer>(
                      ${builderType:L} builder,
                      string key,
                      ref TDeserializer deserializer) =>
                      builder[key] = ${read:L};
              }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  public static void writeSimpleSchema(CSharpWriter writer, Shape shape) {
    writer.pushState();
    try {
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("type", CSharpNaming.typeName(shape.getId().getName()));
      if (shape.getType() == ShapeType.ENUM) {
        writer.putContext("schemas", RuntimeTypes.SCHEMAS);
        writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
        writer.putContext("values", stringEnumValuesExpr(shape));
        writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
        writer.putContext("internalValues", stringEnumInternalValuesExpr(shape));
        writer.putContext(
            "initializer",
            writer.format(
                "${schemas:T}.StringEnum<${type:L}>(${shapeId:L}, values: ${values:L}, traits:"
                    + " ${traits:L}, internalValues: ${internalValues:L})"));
      } else {
        String prelude = primitiveTypeToPreludeSchema(writer, shape.getType());
        writer.putContext(
            "initializer",
            prelude != null ? prelude : writer.format("$T.String", RuntimeTypes.SCHEMAS));
      }
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public static ${schema:T}<${type:L}> Schema { get; } = ${initializer:L};
          }
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * Writes a union's schema as a class deriving from {@code UnionSchema}: the cases are declared
   * once, and each case is told apart, read, and written by the generated variant type it maps to.
   */
  public static void writeUnionSchema(
      CSharpWriter writer, GenerationContext context, UnionShape shape, List<MemberShape> members) {
    Symbol unionType = context.symbolProvider().toSymbol(shape);
    writer.pushState();
    try {
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("generatedSchema", GENERATED_SCHEMA);
      writer.putContext("type", unionType);
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("unionSchema", RuntimeTypes.UNION_SCHEMA);
      writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
      writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
      writer.putContext(
          "caseDeclarations",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  MemberShape member = members.get(index);
                  String traits = memberTraitsExpr(w, context, member);
                  w.write(
                      "new($L, Target$L$L),",
                      CSharpNaming.formatString(member.getMemberName()),
                      index,
                      "null".equals(traits) ? "" : ", " + traits);
                }
              }));
      writer.putContext(
          "targets",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  MemberShape member = members.get(index);
                  w.write(
                      "private static readonly $T<$L> Target$L = $L;",
                      RuntimeTypes.SCHEMA,
                      ShapeSupport.memberTypeExpr(
                          w, context.model(), context.symbolProvider(), member, false),
                      index,
                      rawMemberTargetExpr(w, context, member));
                }
              }));
      writer.putContext(
          "caseOf",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  w.write(
                      "$T.$L => $L,",
                      unionType,
                      CSharpNaming.typeName(members.get(index).getMemberName()),
                      index);
                }
              }));
      writer.putContext(
          "writes",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  MemberShape member = members.get(index);
                  w.write(
                      "case $T.$L @case:",
                      unionType,
                      CSharpNaming.typeName(member.getMemberName()));
                  w.indent();
                  writeValue(
                      w,
                      context,
                      member,
                      String.valueOf(index),
                      "@case.Value",
                      "value" + index,
                      ShapeSupport.isReferenceType(context.model(), member),
                      null,
                      "Target" + index);
                  w.write("break;");
                  w.dedent();
                }
              }));
      writer.putContext(
          "reads",
          writer.consumer(
              w -> {
                for (int index = 0; index < members.size(); index++) {
                  w.write(
                      "$L => new $T.$L(($L)!),",
                      index,
                      unionType,
                      CSharpNaming.typeName(members.get(index).getMemberName()),
                      readValue(
                          w,
                          context,
                          members.get(index),
                          "value" + index,
                          false,
                          "Target" + index));
                }
              }));
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public static ${unionSchema:T}<${type:T}> Schema { get; } = new ${generatedSchema:L}();

              private sealed class ${generatedSchema:L}()
                  : ${unionSchema:T}<${type:T}>(
                      ${shapeId:L},
                      [
                          ${caseDeclarations:C|}
                      ],
                      ${traits:L})
              {
                  ${targets:C|}

                  public override int CaseOf(${type:T} value) =>
                      value switch
                      {
                          ${caseOf:C|}
                          _ => throw NoCaseMatched(),
                      };

                  public override void SerializeCase<TSerializer>(
                      ${type:T} value,
                      ref TSerializer serializer)
                  {
                      switch (value)
                      {
                          ${writes:C|}
                          default:
                              throw NoCaseMatched();
                      }
                  }

                  public override ${type:T} DeserializeCase<TDeserializer>(
                      int index,
                      ref TDeserializer deserializer) =>
                      index switch
                      {
                          ${reads:C|}
                          _ => throw NoCaseMatched(),
                      };
              }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  public static void writeIntEnumSchema(CSharpWriter writer, IntEnumShape shape) {
    writer.pushState();
    try {
      String typeName = CSharpNaming.typeName(shape.getId().getName());
      writer.putContext("schemaClass", localSchemaClassName(shape));
      writer.putContext("typeName", typeName);
      writer.putContext("schema", RuntimeTypes.SCHEMA);
      writer.putContext("schemas", RuntimeTypes.SCHEMAS);
      writer.putContext("shapeId", shapeIdExpr(writer, shape.getId()));
      writer.putContext("values", intEnumValuesExpr(shape));
      writer.putContext("traits", traitsExpr(writer, shape.getAllTraits().values()));
      writer.write(
          """
          public static partial class ${schemaClass:L}
          {
              public static ${schema:T}<${typeName:L}> Schema { get; } =
                  ${schemas:T}.IntEnum<${typeName:L}>(
                      ${shapeId:L}, values: ${values:L}, traits: ${traits:L});
          }
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * The values an enum shape defines, so the runtime can tell a modeled value from one a peer
   * invented. Generated enum types stay open, so the schema is the only place this is recorded.
   */
  public static String stringEnumValuesExpr(Shape shape) {
    return shape
        .asEnumShape()
        .map(
            e ->
                e.getEnumValues().values().stream()
                    .map(CSharpNaming::formatString)
                    .collect(Collectors.joining(", ", "[", "]")))
        .orElse("null");
  }

  /**
   * The values an enum shape marks {@code @internal}. They are valid on the wire like any other,
   * but a server leaves them out of the message it sends back when it rejects a value, so the
   * schema has to record which ones they are.
   */
  public static String stringEnumInternalValuesExpr(Shape shape) {
    return shape
        .asEnumShape()
        .map(
            e ->
                e.getAllMembers().values().stream()
                    .filter(m -> m.hasTrait(InternalTrait.class))
                    .map(m -> m.expectTrait(EnumValueTrait.class).expectStringValue())
                    .map(CSharpNaming::formatString)
                    .collect(Collectors.toList()))
        .filter(values -> !values.isEmpty())
        .map(values -> String.join(", ", values))
        .map(values -> "[" + values + "]")
        .orElse("null");
  }

  /** The int values an intEnum shape defines. See {@link #stringEnumValuesExpr}. */
  public static String intEnumValuesExpr(IntEnumShape shape) {
    return shape.getEnumValues().values().stream()
        .map(String::valueOf)
        .collect(Collectors.joining(", ", "[", "]"));
  }

  private SchemaGenerator() {}

  private static void writeStructureBuilderProperties(
      CSharpWriter writer, GenerationContext context, List<MemberShape> members) {
    for (MemberShape member : members) {
      writer.write(
          "public $L $L { get; set; }",
          ShapeSupport.memberTypeExpr(
              writer, context.model(), context.symbolProvider(), member, true),
          CSharpNaming.propertyName(member.getMemberName()));
    }
  }

  /**
   * The object initializer that starts a builder at its members' modeled defaults, so a member the
   * input leaves out keeps its default. Each call builds new values, so no two deserialized objects
   * share a mutable default.
   */
  private static String builderDefaults(
      CSharpWriter writer, GenerationContext context, List<MemberShape> members) {
    List<String> assignments = new ArrayList<>();
    for (MemberShape member : members) {
      String literal = defaultLiteral(writer, context, member);
      if (literal != null) {
        assignments.add(CSharpNaming.propertyName(member.getMemberName()) + " = " + literal);
      }
    }
    return assignments.isEmpty() ? "" : " { " + String.join(", ", assignments) + " }";
  }

  /**
   * The C# expression for a member's modeled default, or null when it has none. A
   * {@code @clientOptional} member has none here: its default belongs to the server, which a client
   * must not assume.
   */
  private static String defaultLiteral(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    if (!ShapeSupport.hasDefault(member) || ShapeSupport.hasClientOptional(member)) {
      return null;
    }
    Node node = member.expectTrait(DefaultTrait.class).toNode();
    if (node.isNullNode()) {
      return null;
    }
    Model model = context.model();
    Shape target = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return writer.typeName(RuntimeTypes.STREAM) + ".Null";
    }
    String type = writer.typeName(context.symbolProvider().toSymbol(target));
    return switch (target.getType()) {
      case BOOLEAN -> String.valueOf(node.expectBooleanNode().getValue());
      case BYTE -> "(sbyte)" + number(node);
      case SHORT -> "(short)" + number(node);
      case INTEGER -> number(node);
      case LONG -> number(node) + "L";
      case FLOAT -> number(node) + "f";
      case DOUBLE -> number(node) + "d";
      case BIG_DECIMAL -> number(node) + "m";
      case BIG_INTEGER ->
          "global::System.Numerics.BigInteger.Parse("
              + CSharpNaming.formatString(number(node))
              + ", global::System.Globalization.CultureInfo.InvariantCulture)";
      case STRING -> CSharpNaming.formatString(node.expectStringNode().getValue());
      case ENUM ->
          type
              + ".FromValue("
              + CSharpNaming.formatString(node.expectStringNode().getValue())
              + ")";
      case INT_ENUM -> "(" + type + ")" + number(node);
      case BLOB -> {
        String base64 = node.expectStringNode().getValue();
        yield base64.isEmpty()
            ? "[]"
            : "global::System.Convert.FromBase64String(" + CSharpNaming.formatString(base64) + ")";
      }
      case TIMESTAMP -> timestampLiteral(node);
      case DOCUMENT -> documentExpr(writer, node);
      case LIST, SET -> type + ".FromOwnedList([])";
      case MAP -> {
        MapShape map = target.asMapShape().orElseThrow();
        Shape valueTarget = model.expectShape(map.getValue().getTarget());
        String valueType =
            writer.typeName(context.symbolProvider().toSymbol(valueTarget))
                + (ShapeSupport.isSparse(map) ? "?" : "");
        yield type
            + ".FromOwnedDictionary(new "
            + writer.typeName(RuntimeTypes.DICTIONARY)
            + "<string, "
            + valueType
            + ">("
            + writer.typeName(RuntimeTypes.STRING_COMPARER)
            + ".Ordinal))";
      }
      default -> null;
    };
  }

  private static String number(Node node) {
    return new BigDecimal(node.expectNumberNode().getValue().toString())
        .stripTrailingZeros()
        .toPlainString();
  }

  /** A timestamp default is epoch seconds, possibly fractional, or a date-time string. */
  private static String timestampLiteral(Node node) {
    BigDecimal seconds =
        node.isNumberNode()
            ? new BigDecimal(node.expectNumberNode().getValue().toString())
            : epochSeconds(node.expectStringNode().getValue());
    if (seconds.stripTrailingZeros().scale() <= 0) {
      return "global::System.DateTimeOffset.FromUnixTimeSeconds(" + seconds.toBigInteger() + "L)";
    }
    return "global::System.DateTimeOffset.UnixEpoch.AddTicks("
        + seconds.movePointRight(7).longValue()
        + "L)";
  }

  private static BigDecimal epochSeconds(String dateTime) {
    Instant instant = OffsetDateTime.parse(dateTime).toInstant();
    return BigDecimal.valueOf(instant.getEpochSecond())
        .add(BigDecimal.valueOf(instant.getNano(), 9));
  }

  /**
   * Writes the value {@code value} as member {@code index}, calling the serializer directly for the
   * member's kind so no schema sits between the generated code and the serializer. A value that may
   * be null is checked first: it is written as its modeled default {@code defaultValue} when the
   * serializer writes defaults, and as an explicit null otherwise. A kind with no direct call goes
   * through its target schema, {@code fallback}.
   */
  private static void writeValue(
      CSharpWriter writer,
      GenerationContext context,
      MemberShape member,
      String index,
      String value,
      String local,
      boolean mayBeNull,
      String defaultValue,
      String fallback) {
    String call = writeCall(writer, context, member, index, mayBeNull ? local : value);
    if (call == null) {
      writer.write("$L.Write($L, $L, ref serializer);", fallback, index, value);
    } else if (!mayBeNull) {
      writer.write("$L", call);
    } else if (defaultValue != null) {
      writer.write(
          """
          if ($L is { } $L)
          {
              $L
          }
          else if (serializer.WritesDefault($L))
          {
              $L
          }
          else
          {
              serializer.WriteNull($L);
          }\
          """,
          value,
          local,
          call,
          index,
          writeCall(writer, context, member, index, defaultValue),
          index);
    } else {
      writer.write(
          """
          if ($L is { } $L)
          {
              $L
          }
          else
          {
              serializer.WriteNull($L);
          }\
          """,
          value,
          local,
          call,
          index);
    }
  }

  private static String writeCall(
      CSharpWriter writer,
      GenerationContext context,
      MemberShape member,
      String index,
      String value) {
    Model model = context.model();
    Shape target = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "serializer.WriteStream(" + index + ", " + value + ");";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return "serializer.WriteEventStream("
          + index
          + ", "
          + value
          + ", "
          + concreteSchemaAccessor(writer, context, target)
          + ");";
    }
    if (ShapeSupport.isUnit(target.getId())) {
      return null;
    }
    String method =
        switch (target.getType()) {
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
    if (method != null) {
      return "serializer." + method + "(" + index + ", " + value + ");";
    }
    return switch (target.getType()) {
      case ENUM -> "serializer.WriteStringEnum(" + index + ", " + value + ".Value);";
      case INT_ENUM -> "serializer.WriteIntEnum(" + index + ", (int)" + value + ");";
      case STRUCTURE, UNION, LIST, SET, MAP ->
          "serializer."
              + switch (target.getType()) {
                case STRUCTURE -> "WriteStruct";
                case UNION -> "WriteUnion";
                case MAP -> "WriteMap";
                default -> "WriteList";
              }
              + "("
              + index
              + ", "
              + value
              + ", "
              + concreteSchemaAccessor(writer, context, target)
              + ");";
      default -> null;
    };
  }

  /**
   * The expression reading a member's value, calling the deserializer directly for the member's
   * kind. {@code nullableValueType} reads an explicit null as null, which a value type cannot
   * otherwise hold; a reference type is read as it is.
   */
  private static String readValue(
      CSharpWriter writer,
      GenerationContext context,
      MemberShape member,
      String local,
      boolean nullableValueType,
      String fallback) {
    String read = readCall(writer, context, member, local);
    if (read == null) {
      return fallback + ".Read(ref deserializer)";
    }
    return nullableValueType ? "deserializer.TryReadNull() ? null : " + read : read;
  }

  private static String readCall(
      CSharpWriter writer, GenerationContext context, MemberShape member, String local) {
    Model model = context.model();
    Shape target = model.expectShape(member.getTarget());
    if (ShapeSupport.isStreamingBlobMember(model, member)) {
      return "deserializer.ReadStream()";
    }
    if (ShapeSupport.isEventStreamMember(model, member)) {
      return "deserializer.ReadEventStream("
          + concreteSchemaAccessor(writer, context, target)
          + ")";
    }
    if (ShapeSupport.isUnit(target.getId())) {
      return null;
    }
    String type = writer.typeName(context.symbolProvider().toSymbol(target));
    return switch (target.getType()) {
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
      // A format whose enums are ordinals (protobuf) has no value for an ordinal the model does
      // not know, and reads it as null.
      case ENUM ->
          "(deserializer.ReadStringEnum() is { } "
              + local
              + " ? "
              + type
              + ".FromValue("
              + local
              + ") : default("
              + type
              + "))";
      case INT_ENUM -> "(" + type + ")deserializer.ReadIntEnum()";
      case STRUCTURE ->
          "deserializer.ReadStruct(" + concreteSchemaAccessor(writer, context, target) + ")";
      case UNION ->
          "deserializer.ReadUnion(" + concreteSchemaAccessor(writer, context, target) + ")";
      case LIST, SET ->
          "deserializer.ReadList(" + concreteSchemaAccessor(writer, context, target) + ")";
      case MAP -> "deserializer.ReadMap(" + concreteSchemaAccessor(writer, context, target) + ")";
      default -> null;
    };
  }

  /**
   * A generated schema referenced from code that runs after every schema is initialized, so it
   * needs no lazy indirection even when the models are recursive.
   */
  private static String concreteSchemaAccessor(
      CSharpWriter writer, GenerationContext context, Shape target) {
    return schemaClassName(writer, context, target) + ".Schema";
  }

  /**
   * Whether a structure's property can hold null: a reference type always can; a value type only
   * when the property is declared nullable, which for a structure includes a member with a default.
   */
  private static boolean propertyMayBeNull(
      GenerationContext context, MemberShape member, boolean isError) {
    if (ShapeSupport.isReferenceType(context.model(), member)) {
      return true;
    }
    return ShapeSupport.isNullable(member) || (!isError && ShapeSupport.hasDefault(member));
  }

  private static void writeStructureMemberDeclarations(
      CSharpWriter writer, GenerationContext context, List<MemberShape> members) {
    for (int index = 0; index < members.size(); index++) {
      MemberShape member = members.get(index);
      String traits = memberTraitsExpr(writer, context, member);
      writer.write(
          "new($L, Target$L$L$L),",
          CSharpNaming.formatString(member.getMemberName()),
          index,
          ShapeSupport.isRequired(member) ? ", isRequired: true" : "",
          "null".equals(traits) ? "" : ", traits: " + traits);
    }
  }

  private static void writeStructureTargets(
      CSharpWriter writer, GenerationContext context, List<MemberShape> members) {
    for (int index = 0; index < members.size(); index++) {
      MemberShape member = members.get(index);
      writer.write(
          "private static readonly $T<$L> Target$L = $L;",
          RuntimeTypes.SCHEMA,
          ShapeSupport.memberTypeExpr(
              writer, context.model(), context.symbolProvider(), member, true),
          index,
          memberTargetExpr(writer, context, member));
    }
  }

  private static boolean isCycleCapable(ShapeType type) {
    return switch (type) {
      case STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  private static String supportedPreludeSchemaShapeNames() {
    return String.join(
        ", ",
        "Boolean",
        "Byte",
        "Short",
        "Integer",
        "Long",
        "Float",
        "Double",
        "BigInteger",
        "BigDecimal",
        "String",
        "Blob",
        "Timestamp",
        "Document",
        "Unit");
  }

  private static String localSchemaClassName(Shape shape) {
    return CSharpNaming.typeName(shape.getId().getName()) + "Schema";
  }

  private static String memberTargetExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    String targetExpr = targetSchemaExpr(writer, context, member, target);
    String nullableMemberType =
        ShapeSupport.memberTypeExpr(
            writer, context.model(), context.symbolProvider(), member, true);
    if (!nullableMemberType.endsWith("?")) {
      return targetExpr;
    }

    if (!ShapeSupport.isReferenceType(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".Nullable(") + targetExpr + ")";
    }
    return (writer.typeName(RuntimeTypes.SCHEMAS) + ".NullableReference(") + targetExpr + ")";
  }

  private static String rawMemberTargetExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return targetSchemaExpr(writer, context, member, target);
  }

  private static String targetSchemaExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member, Shape target) {
    if (ShapeSupport.isStreamingBlobMember(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".StreamingBlob");
    }
    if (ShapeSupport.isEventStreamMember(context.model(), member)) {
      return (writer.typeName(RuntimeTypes.SCHEMAS) + ".EventStream(")
          + shapeSchemaAccessor(writer, context, target)
          + ")";
    }
    return shapeSchemaAccessor(writer, context, target);
  }

  /**
   * Element/value schema accessor for a list or map member, wrapped in a nullable schema when the
   * enclosing collection is {@code @sparse}. Sparse collections carry nullable elements/values in
   * the generated model type, so the schema's element type must match.
   */
  private static String elementSchemaExpr(
      CSharpWriter writer,
      GenerationContext context,
      MemberShape member,
      Shape target,
      boolean sparse) {
    String targetExpr = shapeSchemaAccessor(writer, context, target);
    String base;
    if (!sparse) {
      base = targetExpr;
    } else {
      base =
          ShapeSupport.isReferenceType(context.model(), member)
              ? (writer.typeName(RuntimeTypes.SCHEMAS) + ".NullableReference(") + targetExpr + ")"
              : (writer.typeName(RuntimeTypes.SCHEMAS) + ".Nullable(") + targetExpr + ")";
    }

    return base;
  }

  private static String constructorArguments(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    if (!shape.isStructureShape()) {
      return members.stream()
          .map(m -> "builder." + CSharpNaming.propertyName(m.getMemberName()))
          .collect(Collectors.joining(", "));
    }

    List<String> args = new ArrayList<>();
    MemberShape errorMessageMember = null;
    if (shape.hasTrait(ErrorTrait.class) && shape.isStructureShape()) {
      errorMessageMember =
          ShapeSupport.errorMessageMember(context.model(), shape.asStructureShape().orElseThrow())
              .orElse(null);
      args.add(
          errorMessageMember == null
              ? "null"
              : "builder." + CSharpNaming.propertyName(errorMessageMember.getMemberName()));
    }
    for (MemberShape member :
        ShapeSupport.constructorMembers(shape.asStructureShape().orElseThrow())) {
      if (errorMessageMember != null && member.equals(errorMessageMember)) {
        continue;
      }
      String prop = CSharpNaming.propertyName(member.getMemberName());
      String expr = "builder." + prop;
      if (ShapeSupport.isRequired(member)) {
        Symbol memberSymbol = context.symbolProvider().toSymbol(member);
        boolean memberIsValueType =
            memberSymbol.getProperty(SymbolProperties.IS_VALUE_TYPE, Boolean.class).orElse(false);
        if (memberIsValueType) {
          expr = "builder." + prop + ".GetValueOrDefault()";
        } else {
          // Typed so the server runtime can tell a caller's missing member from a server fault and
          // answer with smithy.framework#ValidationException instead of a 500.
          expr =
              "builder."
                  + prop
                  + (" ?? throw new "
                      + writer.typeName(RuntimeTypes.MISSING_REQUIRED_MEMBER_EXCEPTION)
                      + "(")
                  + CSharpNaming.formatString(member.getMemberName())
                  + ")";
        }
      }
      args.add(expr);
    }
    return String.join(", ", args);
  }

  private static String memberTraitsExpr(
      CSharpWriter writer, GenerationContext context, MemberShape member) {
    List<Trait> traits = new ArrayList<>(member.getAllTraits().values());
    Shape target = context.model().expectShape(member.getTarget());
    if (shouldInlineTargetTraits(target)) {
      for (Trait trait : target.getAllTraits().values()) {
        if (traits.stream().noneMatch(existing -> existing.toShapeId().equals(trait.toShapeId()))) {
          traits.add(trait);
        }
      }
    }
    return traitsExpr(writer, traits);
  }

  private static boolean shouldInlineTargetTraits(Shape target) {
    if ("smithy.api".equals(target.getId().getNamespace())) {
      return false;
    }

    return switch (target.getType()) {
      case BOOLEAN,
          BYTE,
          SHORT,
          INTEGER,
          LONG,
          FLOAT,
          DOUBLE,
          BIG_INTEGER,
          BIG_DECIMAL,
          STRING,
          BLOB,
          TIMESTAMP,
          DOCUMENT ->
          true;
      default -> false;
    };
  }

  private static String documentExpr(CSharpWriter writer, Node node) {
    return switch (node.getType()) {
      case NULL -> (writer.typeName(RuntimeTypes.DOCUMENT) + ".Null");
      case BOOLEAN ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
              + node.expectBooleanNode().getValue()
              + ")";
      case STRING ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
              + CSharpNaming.formatString(node.expectStringNode().getValue())
              + ")";
      case NUMBER ->
          (writer.typeName(RuntimeTypes.DOCUMENT) + ".From((decimal)")
              + node.expectNumberNode().getValue()
              + ")";
      case ARRAY -> arrayDocumentExpr(writer, node.expectArrayNode());
      case OBJECT -> objectDocumentExpr(writer, node.expectObjectNode());
    };
  }

  private static String arrayDocumentExpr(CSharpWriter writer, ArrayNode node) {
    return (writer.typeName(RuntimeTypes.DOCUMENT)
            + ".From(new "
            + writer.typeName(RuntimeTypes.DOCUMENT)
            + "[] {")
        + node.getElements().stream()
            .map(value -> documentExpr(writer, value))
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectDocumentExpr(CSharpWriter writer, ObjectNode node) {
    return (writer.typeName(RuntimeTypes.DOCUMENT) + ".From(")
        + "new "
        + writer.typeName(RuntimeTypes.DICTIONARY)
        + ("<string, " + writer.typeName(RuntimeTypes.DOCUMENT) + ">")
        + " {"
        + node.getMembers().entrySet().stream()
            .map(value -> objectMemberExpr(writer, value))
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectMemberExpr(CSharpWriter writer, Map.Entry<StringNode, Node> member) {
    return "{"
        + CSharpNaming.formatString(member.getKey().getValue())
        + ", "
        + documentExpr(writer, member.getValue())
        + "}";
  }

  private static String primitiveTypeToPreludeSchema(CSharpWriter writer, ShapeType t) {
    return switch (t) {
      case BOOLEAN -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Boolean");
      case BYTE -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Byte");
      case SHORT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Short");
      case INTEGER -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Integer");
      case LONG -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Long");
      case FLOAT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Float");
      case DOUBLE -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Double");
      case BIG_INTEGER -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigInteger");
      case BIG_DECIMAL -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".BigDecimal");
      case STRING -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".String");
      case BLOB -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Blob");
      case TIMESTAMP -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Timestamp");
      case DOCUMENT -> (writer.typeName(RuntimeTypes.SCHEMAS) + ".Document");
      default -> null;
    };
  }
}

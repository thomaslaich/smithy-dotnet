package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.SymbolProperties;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.node.StringNode;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class SchemaGenerator {

  private SchemaGenerator() {}

  public static void addFunctionalImports(CSharpWriter writer) {
    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
  }

  public static String functionalShapeSchemaAccessor(GenerationContext context, Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return switch (shape.getId().getName()) {
        case "Boolean" -> "FunctionalSchemas.Boolean";
        case "Byte" -> "FunctionalSchemas.Byte";
        case "Short" -> "FunctionalSchemas.Short";
        case "Integer" -> "FunctionalSchemas.Integer";
        case "Long" -> "FunctionalSchemas.Long";
        case "Float" -> "FunctionalSchemas.Float";
        case "Double" -> "FunctionalSchemas.Double";
        case "BigInteger" -> "FunctionalSchemas.BigInteger";
        case "BigDecimal" -> "FunctionalSchemas.BigDecimal";
        case "String" -> "FunctionalSchemas.String";
        case "Blob" -> "FunctionalSchemas.Blob";
        case "Timestamp" -> "FunctionalSchemas.Timestamp";
        case "Document" -> "FunctionalSchemas.Document";
        case "Unit" -> "FunctionalSchemas.Unit";
        default ->
            throw new IllegalArgumentException("Unsupported prelude shape: " + shape.getId());
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
          ? "FunctionalSchemas.Timestamp"
          : "FunctionalSchemas.TimestampWithTraits(" + traitsExpr(tsTraits) + ")";
    }

    String preludeSchema =
        shape.getType() == ShapeType.ENUM
            ? null
            : primitiveTypeToFunctionalPreludeSchema(shape.getType());
    if (preludeSchema != null) {
      return preludeSchema;
    }

    String accessor = functionalSchemaClassName(context, shape) + ".FunctionalSchema";

    // Aggregate shapes can participate in recursive graphs (a shape referencing itself
    // directly or through a cycle). A direct static reference would observe null while the
    // referenced schema's static initializer is still running, so defer it lazily. The
    // null-forgiving '!' suppresses the nullable-flow warning for self-references where the
    // property is not yet definitely assigned at the point the lambda is created.
    return isCycleCapable(shape.getType())
        ? "FunctionalSchemas.Lazy(() => " + accessor + "!)"
        : accessor;
  }

  private static boolean isCycleCapable(ShapeType type) {
    return switch (type) {
      case STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  public static String functionalSchemaClassName(GenerationContext context, Shape shape) {
    return CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape)) + "Schema";
  }

  public static String functionalOperationSchemaAccessor(
      GenerationContext context, OperationShape shape) {
    return CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape))
        + "Schema.FunctionalSchema";
  }

  public static String functionalOperationShapeType(GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return "SmithyUnit";
    return CSharpSymbolProvider.qualified(
        context.symbolProvider().toSymbol(context.model().expectShape(id)));
  }

  public static String functionalOperationShapeSchema(GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return "FunctionalSchemas.Unit";
    return functionalShapeSchemaAccessor(context, context.model().expectShape(id));
  }

  private static String localSchemaClassName(Shape shape) {
    return CSharpNaming.typeName(shape.getId().getName()) + "Schema";
  }

  public static String shapeIdExpr(ShapeId id) {
    return "ShapeId.Parse(" + CSharpNaming.formatString(id.toString()) + ")";
  }

  public static String traitExpr(Trait trait) {
    String idExpr = shapeIdExpr(trait.toShapeId());
    Node node = trait.toNode();
    if (node.isNullNode()) {
      return "new Trait(" + idExpr + ")";
    }

    return "new Trait(" + idExpr + ", " + documentExpr(node) + ")";
  }

  public static String traitsExpr(Collection<? extends Trait> traits) {
    if (traits.isEmpty()) {
      return "null";
    }

    List<Trait> sorted = new ArrayList<>(traits);
    sorted.sort(java.util.Comparator.comparing(t -> t.toShapeId().toString()));
    return "["
        + sorted.stream().map(SchemaGenerator::traitExpr).collect(Collectors.joining(", "))
        + "]";
  }

  public static void writeFunctionalStructureSchema(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    addFunctionalImports(writer);
    SymbolProvider sp = context.symbolProvider();
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public sealed class FunctionalBuilder");
          writer.openBlock(
              "{",
              "}",
              () -> {
                for (MemberShape member : members) {
                  writer.write(
                      "public $L $L { get; set; }",
                      ShapeSupport.memberTypeExpr(sp, member, true),
                      CSharpNaming.propertyName(member.getMemberName()));
                }
              });
          writer.write("");
          writer.write("public static FunctionalSchema<$L> FunctionalSchema { get; } =", typeName);
          writer.indent();
          writer.write(
              "FunctionalSchemas.Structure<$L, FunctionalBuilder>($L, $L)",
              typeName,
              shapeIdExpr(shape.getId()),
              traitsExpr(shape.getAllTraits().values()));
          writer.indent();
          for (MemberShape member : members) {
            String method = ShapeSupport.isRequired(member) ? "Required" : "Optional";
            String name = member.getMemberName();
            String prop = CSharpNaming.propertyName(name);
            writer.write(".$L(", method);
            writer.indent();
            writer.write("$L,", CSharpNaming.formatString(name));
            writer.write("static value => value.$L,", prop);
            writer.write("static (builder, value) => builder.$L = value,", prop);
            writer.write("$L,", functionalMemberTargetExpr(context, member));
            writer.write("$L)", memberTraitsExpr(context, member));
            writer.dedent();
          }
          writer.write(".Build(");
          writer.indent();
          writer.write("static () => new FunctionalBuilder(),");
          writer.write(
              "static builder => new $L($L))",
              typeName,
              functionalConstructorArguments(context, shape, members));
          writer.dedent();
          writer.write(";");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeFunctionalListSchema(
      CSharpWriter writer, GenerationContext context, ListShape shape) {
    addFunctionalImports(writer);
    SymbolProvider sp = context.symbolProvider();
    Shape memberTarget = context.model().expectShape(shape.getMember().getTarget());
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String memberType =
        CSharpSymbolProvider.qualified(sp.toSymbol(memberTarget)) + (sparse ? "?" : "");
    String builderType = "System.Collections.Generic.List<" + memberType + ">";
    String factory = shape.getType() == ShapeType.SET ? "Set" : "List";
    String elementSchema =
        functionalElementSchemaExpr(context, shape.getMember(), memberTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static FunctionalSchema<$L> FunctionalSchema { get; } =", typeName);
          writer.indent();
          writer.write(
              "FunctionalSchemas.$L<$L, $L, $L>($L, $L,",
              factory,
              typeName,
              memberType,
              builderType,
              shapeIdExpr(shape.getId()),
              elementSchema);
          writer.indent();
          writer.write("static value => value.Values,");
          writer.write("static () => new $L(),", builderType);
          writer.write("static (builder, value) => builder.Add(value),");
          writer.write("static builder => new $L(builder),", typeName);
          writer.write("$L);", traitsExpr(shape.getAllTraits().values()));
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeFunctionalMapSchema(
      CSharpWriter writer, GenerationContext context, MapShape shape) {
    addFunctionalImports(writer);
    SymbolProvider sp = context.symbolProvider();
    Shape valueTarget = context.model().expectShape(shape.getValue().getTarget());
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String valueType =
        CSharpSymbolProvider.qualified(sp.toSymbol(valueTarget)) + (sparse ? "?" : "");
    String builderType = "System.Collections.Generic.Dictionary<string, " + valueType + ">";
    String valueSchema =
        functionalElementSchemaExpr(context, shape.getValue(), valueTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static FunctionalSchema<$L> FunctionalSchema { get; } =", typeName);
          writer.indent();
          writer.write(
              "FunctionalSchemas.Map<$L, $L, $L>($L, $L,",
              typeName,
              valueType,
              builderType,
              shapeIdExpr(shape.getId()),
              valueSchema);
          writer.indent();
          writer.write("static value => value.Values,");
          writer.write("static () => new $L(System.StringComparer.Ordinal),", builderType);
          writer.write("static (builder, key, value) => builder[key] = value,");
          writer.write("static builder => new $L(builder),", typeName);
          writer.write("$L);", traitsExpr(shape.getAllTraits().values()));
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeFunctionalSimpleSchema(CSharpWriter writer, Shape shape) {
    addFunctionalImports(writer);
    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (shape.getType() == ShapeType.ENUM) {
            String typeName = CSharpNaming.typeName(shape.getId().getName());
            writer.write(
                "public static FunctionalSchema<$L> FunctionalSchema { get; } ="
                    + " FunctionalSchemas.StringEnum<$L>($L, traits: $L);",
                typeName,
                typeName,
                shapeIdExpr(shape.getId()),
                traitsExpr(shape.getAllTraits().values()));
            writer.write("");
            return;
          }

          String prelude = primitiveTypeToFunctionalPreludeSchema(shape.getType());
          if (prelude == null) {
            prelude = "FunctionalSchemas.String";
          }
          writer.write(
              "public static FunctionalSchema<$L> FunctionalSchema { get; } = $L;",
              CSharpNaming.typeName(shape.getId().getName()),
              prelude);
          writer.write("");
        });
  }

  public static void writeFunctionalUnionSchema(
      CSharpWriter writer, GenerationContext context, UnionShape shape, List<MemberShape> members) {
    addFunctionalImports(writer);
    String typeName = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static FunctionalSchema<$L> FunctionalSchema { get; } =", typeName);
          writer.indent();
          writer.write(
              "FunctionalSchemas.Union<$L>($L, $L)",
              typeName,
              shapeIdExpr(shape.getId()),
              traitsExpr(shape.getAllTraits().values()));
          writer.indent();
          for (MemberShape member : members) {
            String variantName = CSharpNaming.typeName(member.getMemberName());
            writer.write(".Case(");
            writer.indent();
            writer.write("$L,", CSharpNaming.formatString(member.getMemberName()));
            writer.write("static value => value is $L.$L,", typeName, variantName);
            writer.write("static value => (($L.$L)value).Value,", typeName, variantName);
            writer.write("static value => new $L.$L(value!),", typeName, variantName);
            writer.write("$L,", functionalRawMemberTargetExpr(context, member));
            writer.write("$L)", memberTraitsExpr(context, member));
            writer.dedent();
          }
          writer.write(".Build();");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  private static String functionalMemberTargetExpr(GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    String targetExpr = functionalTargetSchemaExpr(context, member, target);
    String nullableMemberType = ShapeSupport.memberTypeExpr(context.symbolProvider(), member, true);
    if (!nullableMemberType.endsWith("?")) {
      return targetExpr;
    }

    if (!ShapeSupport.isReferenceType(context.model(), member)) {
      return "FunctionalSchemas.Nullable(" + targetExpr + ")";
    }
    return "FunctionalSchemas.NullableReference(" + targetExpr + ")";
  }

  private static String functionalRawMemberTargetExpr(
      GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return functionalTargetSchemaExpr(context, member, target);
  }

  /**
   * Target schema for a member, honoring a member-level {@code @timestampFormat} (which takes
   * precedence over the target shape's format). Baking the format into the target schema lets the
   * codec resolve it from the schema without threading member traits.
   */
  private static String functionalTargetSchemaExpr(
      GenerationContext context, MemberShape member, Shape target) {
    if (target.getType() == ShapeType.TIMESTAMP) {
      List<Trait> memberFormat =
          member.getAllTraits().values().stream()
              .filter(t -> t.toShapeId().toString().equals("smithy.api#timestampFormat"))
              .collect(Collectors.toList());
      if (!memberFormat.isEmpty()) {
        return "FunctionalSchemas.TimestampWithTraits(" + traitsExpr(memberFormat) + ")";
      }
    }
    return functionalShapeSchemaAccessor(context, target);
  }

  /**
   * Element/value schema accessor for a list or map member, wrapped in a nullable schema when the
   * enclosing collection is {@code @sparse}. Sparse collections carry nullable elements/values in
   * the generated model type, so the schema's element type must match.
   */
  private static String functionalElementSchemaExpr(
      GenerationContext context, MemberShape member, Shape target, boolean sparse) {
    String targetExpr = functionalShapeSchemaAccessor(context, target);
    if (!sparse) {
      return targetExpr;
    }
    return ShapeSupport.isReferenceType(context.model(), member)
        ? "FunctionalSchemas.NullableReference(" + targetExpr + ")"
        : "FunctionalSchemas.Nullable(" + targetExpr + ")";
  }

  private static String functionalConstructorArguments(
      GenerationContext context, Shape shape, List<MemberShape> members) {
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
          expr =
              "builder."
                  + prop
                  + " ?? throw new System.InvalidOperationException("
                  + CSharpNaming.formatString(
                      "Missing required member '" + member.getMemberName() + "'.")
                  + ")";
        }
      }
      args.add(expr);
    }
    return String.join(", ", args);
  }

  private static String memberTraitsExpr(GenerationContext context, MemberShape member) {
    List<Trait> traits = new ArrayList<>(member.getAllTraits().values());
    Shape target = context.model().expectShape(member.getTarget());
    if (shouldInlineTargetTraits(target)) {
      for (Trait trait : target.getAllTraits().values()) {
        if (traits.stream().noneMatch(existing -> existing.toShapeId().equals(trait.toShapeId()))) {
          traits.add(trait);
        }
      }
    }
    return traitsExpr(traits);
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

  private static String documentExpr(Node node) {
    return switch (node.getType()) {
      case NULL -> "NSmithy.Core.Document.Null";
      case BOOLEAN -> "NSmithy.Core.Document.From(" + node.expectBooleanNode().getValue() + ")";
      case STRING ->
          "NSmithy.Core.Document.From("
              + CSharpNaming.formatString(node.expectStringNode().getValue())
              + ")";
      case NUMBER ->
          "NSmithy.Core.Document.From((decimal)" + node.expectNumberNode().getValue() + ")";
      case ARRAY -> arrayDocumentExpr(node.expectArrayNode());
      case OBJECT -> objectDocumentExpr(node.expectObjectNode());
    };
  }

  private static String arrayDocumentExpr(ArrayNode node) {
    return "NSmithy.Core.Document.From(new NSmithy.Core.Document[] {"
        + node.getElements().stream()
            .map(SchemaGenerator::documentExpr)
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectDocumentExpr(ObjectNode node) {
    return "NSmithy.Core.Document.From("
        + "new System.Collections.Generic.Dictionary<string, NSmithy.Core.Document>"
        + " {"
        + node.getMembers().entrySet().stream()
            .map(SchemaGenerator::objectMemberExpr)
            .collect(Collectors.joining(", "))
        + "})";
  }

  private static String objectMemberExpr(Map.Entry<StringNode, Node> member) {
    return "{"
        + CSharpNaming.formatString(member.getKey().getValue())
        + ", "
        + documentExpr(member.getValue())
        + "}";
  }


  private static String primitiveTypeToFunctionalPreludeSchema(ShapeType t) {
    return switch (t) {
      case BOOLEAN -> "FunctionalSchemas.Boolean";
      case BYTE -> "FunctionalSchemas.Byte";
      case SHORT -> "FunctionalSchemas.Short";
      case INTEGER -> "FunctionalSchemas.Integer";
      case LONG -> "FunctionalSchemas.Long";
      case FLOAT -> "FunctionalSchemas.Float";
      case DOUBLE -> "FunctionalSchemas.Double";
      case BIG_INTEGER -> "FunctionalSchemas.BigInteger";
      case BIG_DECIMAL -> "FunctionalSchemas.BigDecimal";
      case STRING -> "FunctionalSchemas.String";
      case BLOB -> "FunctionalSchemas.Blob";
      case TIMESTAMP -> "FunctionalSchemas.Timestamp";
      case DOCUMENT -> "FunctionalSchemas.Document";
      default -> null;
    };
  }

}

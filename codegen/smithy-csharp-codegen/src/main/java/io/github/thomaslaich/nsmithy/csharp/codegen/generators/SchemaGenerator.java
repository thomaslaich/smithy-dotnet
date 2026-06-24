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

  public static void addImports(CSharpWriter writer) {
    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
  }

  public static String shapeSchemaAccessor(GenerationContext context, Shape shape) {
    if ("smithy.api".equals(shape.getId().getNamespace())) {
      return switch (shape.getId().getName()) {
        case "Boolean" -> "Schemas.Boolean";
        case "Byte" -> "Schemas.Byte";
        case "Short" -> "Schemas.Short";
        case "Integer" -> "Schemas.Integer";
        case "Long" -> "Schemas.Long";
        case "Float" -> "Schemas.Float";
        case "Double" -> "Schemas.Double";
        case "BigInteger" -> "Schemas.BigInteger";
        case "BigDecimal" -> "Schemas.BigDecimal";
        case "String" -> "Schemas.String";
        case "Blob" -> "Schemas.Blob";
        case "Timestamp" -> "Schemas.Timestamp";
        case "Document" -> "Schemas.Document";
        case "Unit" -> "Schemas.Unit";
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
          ? "Schemas.Timestamp"
          : "Schemas.TimestampWithTraits(" + traitsExpr(tsTraits) + ")";
    }

    String preludeSchema =
        shape.getType() == ShapeType.ENUM ? null : primitiveTypeToPreludeSchema(shape.getType());
    if (preludeSchema != null) {
      return preludeSchema;
    }

    String accessor = schemaClassName(context, shape) + ".Schema";

    // Aggregate shapes can participate in recursive graphs (a shape referencing itself
    // directly or through a cycle). A direct static reference would observe null while the
    // referenced schema's static initializer is still running, so defer it lazily. The
    // null-forgiving '!' suppresses the nullable-flow warning for self-references where the
    // property is not yet definitely assigned at the point the lambda is created.
    return isCycleCapable(shape.getType()) ? "Schemas.Lazy(() => " + accessor + "!)" : accessor;
  }

  private static boolean isCycleCapable(ShapeType type) {
    return switch (type) {
      case STRUCTURE, UNION, LIST, SET, MAP -> true;
      default -> false;
    };
  }

  public static String schemaClassName(GenerationContext context, Shape shape) {
    return CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape)) + "Schema";
  }

  public static String operationSchemaAccessor(GenerationContext context, OperationShape shape) {
    return CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape))
        + "Schema.Schema";
  }

  public static String serviceSchemaAccessor(
      GenerationContext context, software.amazon.smithy.model.shapes.ServiceShape service) {
    String ns = context.settings().csharpNamespace(service.getId().getNamespace());
    String typeName = CSharpNaming.typeName(service.getId().getName());
    return (ns.isEmpty() ? "" : ns + ".") + typeName + "Schema.Schema";
  }

  public static String operationShapeType(GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return "SmithyUnit";
    return CSharpSymbolProvider.qualified(
        context.symbolProvider().toSymbol(context.model().expectShape(id)));
  }

  public static String operationShapeSchema(GenerationContext context, ShapeId id) {
    if (ShapeSupport.isUnit(id)) return "Schemas.Unit";
    return shapeSchemaAccessor(context, context.model().expectShape(id));
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

  public static void writeStructureSchema(
      CSharpWriter writer, GenerationContext context, Shape shape, List<MemberShape> members) {
    addImports(writer);
    SymbolProvider sp = context.symbolProvider();
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public sealed class Builder");
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
          writer.write("public static Schema<$L> Schema { get; } =", typeName);
          writer.indent();
          writer.write(
              "Schemas.Structure<$L, Builder>($L, $L)",
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
            writer.write("$L,", memberTargetExpr(context, member));
            writer.write("$L)", memberTraitsExpr(context, member));
            writer.dedent();
          }
          writer.write(".Build(");
          writer.indent();
          writer.write("static () => new Builder(),");
          writer.write(
              "static builder => new $L($L))",
              typeName,
              constructorArguments(context, shape, members));
          writer.dedent();
          writer.write(";");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  public static void writeListSchema(
      CSharpWriter writer, GenerationContext context, ListShape shape) {
    addImports(writer);
    SymbolProvider sp = context.symbolProvider();
    Shape memberTarget = context.model().expectShape(shape.getMember().getTarget());
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String memberType =
        CSharpSymbolProvider.qualified(sp.toSymbol(memberTarget)) + (sparse ? "?" : "");
    String builderType = "System.Collections.Generic.List<" + memberType + ">";
    String factory = shape.getType() == ShapeType.SET ? "Set" : "List";
    String elementSchema = elementSchemaExpr(context, shape.getMember(), memberTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static Schema<$L> Schema { get; } =", typeName);
          writer.indent();
          writer.write(
              "Schemas.$L<$L, $L, $L>($L, $L,",
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

  public static void writeMapSchema(
      CSharpWriter writer, GenerationContext context, MapShape shape) {
    addImports(writer);
    SymbolProvider sp = context.symbolProvider();
    Shape valueTarget = context.model().expectShape(shape.getValue().getTarget());
    String typeName = CSharpSymbolProvider.qualified(sp.toSymbol(shape));
    boolean sparse = ShapeSupport.isSparse(shape);
    String valueType =
        CSharpSymbolProvider.qualified(sp.toSymbol(valueTarget)) + (sparse ? "?" : "");
    String builderType = "System.Collections.Generic.Dictionary<string, " + valueType + ">";
    String valueSchema = elementSchemaExpr(context, shape.getValue(), valueTarget, sparse);

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static Schema<$L> Schema { get; } =", typeName);
          writer.indent();
          writer.write(
              "Schemas.Map<$L, $L, $L>($L, $L,",
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

  public static void writeSimpleSchema(CSharpWriter writer, Shape shape) {
    addImports(writer);
    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (shape.getType() == ShapeType.ENUM) {
            String typeName = CSharpNaming.typeName(shape.getId().getName());
            writer.write(
                "public static Schema<$L> Schema { get; } ="
                    + " Schemas.StringEnum<$L>($L, traits: $L);",
                typeName,
                typeName,
                shapeIdExpr(shape.getId()),
                traitsExpr(shape.getAllTraits().values()));
            writer.write("");
            return;
          }

          String prelude = primitiveTypeToPreludeSchema(shape.getType());
          if (prelude == null) {
            prelude = "Schemas.String";
          }
          writer.write(
              "public static Schema<$L> Schema { get; } = $L;",
              CSharpNaming.typeName(shape.getId().getName()),
              prelude);
          writer.write("");
        });
  }

  public static void writeUnionSchema(
      CSharpWriter writer, GenerationContext context, UnionShape shape, List<MemberShape> members) {
    addImports(writer);
    String typeName = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape));

    writer.write("public static partial class $L", localSchemaClassName(shape));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("public static Schema<$L> Schema { get; } =", typeName);
          writer.indent();
          writer.write(
              "Schemas.Union<$L>($L, $L)",
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
            writer.write("$L,", rawMemberTargetExpr(context, member));
            writer.write("$L)", memberTraitsExpr(context, member));
            writer.dedent();
          }
          writer.write(".Build();");
          writer.dedent();
          writer.dedent();
          writer.write("");
        });
  }

  private static String memberTargetExpr(GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    String targetExpr = targetSchemaExpr(context, member, target);
    String nullableMemberType = ShapeSupport.memberTypeExpr(context.symbolProvider(), member, true);
    if (!nullableMemberType.endsWith("?")) {
      return targetExpr;
    }

    if (!ShapeSupport.isReferenceType(context.model(), member)) {
      return "Schemas.Nullable(" + targetExpr + ")";
    }
    return "Schemas.NullableReference(" + targetExpr + ")";
  }

  private static String rawMemberTargetExpr(GenerationContext context, MemberShape member) {
    Shape target = context.model().expectShape(member.getTarget());
    return targetSchemaExpr(context, member, target);
  }

  /**
   * Target schema for a member, honoring a member-level {@code @timestampFormat} (which takes
   * precedence over the target shape's format). Baking the format into the target schema lets the
   * codec resolve it from the schema without threading member traits.
   */
  private static String targetSchemaExpr(
      GenerationContext context, MemberShape member, Shape target) {
    if (target.getType() == ShapeType.TIMESTAMP) {
      List<Trait> memberFormat =
          member.getAllTraits().values().stream()
              .filter(t -> t.toShapeId().toString().equals("smithy.api#timestampFormat"))
              .collect(Collectors.toList());
      if (!memberFormat.isEmpty()) {
        return "Schemas.TimestampWithTraits(" + traitsExpr(memberFormat) + ")";
      }
    }
    return shapeSchemaAccessor(context, target);
  }

  /**
   * Element/value schema accessor for a list or map member, wrapped in a nullable schema when the
   * enclosing collection is {@code @sparse}. Sparse collections carry nullable elements/values in
   * the generated model type, so the schema's element type must match.
   */
  private static String elementSchemaExpr(
      GenerationContext context, MemberShape member, Shape target, boolean sparse) {
    String targetExpr = shapeSchemaAccessor(context, target);
    String base;
    if (!sparse) {
      base = targetExpr;
    } else {
      base =
          ShapeSupport.isReferenceType(context.model(), member)
              ? "Schemas.NullableReference(" + targetExpr + ")"
              : "Schemas.Nullable(" + targetExpr + ")";
    }

    // List/map members carry their own traits (e.g. @xmlName naming each item element in a
    // non-flattened restXml list, or a member-level @timestampFormat). The element is otherwise the
    // shared target schema, which doesn't carry them, so overlay the member's traits onto it.
    String memberTraits = memberTraitsExpr(context, member);
    return "null".equals(memberTraits)
        ? base
        : "Schemas.WithTraits(" + base + ", " + memberTraits + ")";
  }

  private static String constructorArguments(
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

  private static String primitiveTypeToPreludeSchema(ShapeType t) {
    return switch (t) {
      case BOOLEAN -> "Schemas.Boolean";
      case BYTE -> "Schemas.Byte";
      case SHORT -> "Schemas.Short";
      case INTEGER -> "Schemas.Integer";
      case LONG -> "Schemas.Long";
      case FLOAT -> "Schemas.Float";
      case DOUBLE -> "Schemas.Double";
      case BIG_INTEGER -> "Schemas.BigInteger";
      case BIG_DECIMAL -> "Schemas.BigDecimal";
      case STRING -> "Schemas.String";
      case BLOB -> "Schemas.Blob";
      case TIMESTAMP -> "Schemas.Timestamp";
      case DOCUMENT -> "Schemas.Document";
      default -> null;
    };
  }
}

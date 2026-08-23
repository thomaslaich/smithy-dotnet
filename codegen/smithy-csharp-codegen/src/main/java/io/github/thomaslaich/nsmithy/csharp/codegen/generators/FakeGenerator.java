/*
 * Fake handler generator, opt-in via generateFakes. Emits:
 *   - `Fake{Service}Handler : I{Service}Handler` whose methods return canned responses: the
 *     output of the operation's first non-error @examples entry when present, otherwise
 *     placeholder values synthesized from the shapes (self-describing strings, first enum and
 *     union variants, single-element collections, constraint minimums).
 *
 * The class is registered through the ordinary Add{Service}Handler<T>() extension. Its operation
 * methods are virtual so a subclass can replace individual operations; registering a real
 * per-operation handler after the fake works too, since the last DI registration wins.
 *
 * All values are compiled in as literals, so responses are deterministic across calls, runs, and
 * regenerations of an unchanged model.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.Set;
import java.util.logging.Logger;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
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
import software.amazon.smithy.model.traits.ExamplesTrait;
import software.amazon.smithy.model.traits.HttpResponseCodeTrait;
import software.amazon.smithy.model.traits.LengthTrait;
import software.amazon.smithy.model.traits.RangeTrait;
import software.amazon.smithy.model.traits.Trait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class FakeGenerator implements Runnable {

  private static final Logger LOGGER = Logger.getLogger(FakeGenerator.class.getName());

  /** 2024-01-01T00:00:00Z, the fixed timestamp used when no example provides one. */
  private static final long PLACEHOLDER_EPOCH_SECONDS = 1704067200L;

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final List<PendingIterator> pendingIterators = new ArrayList<>();

  private record PendingIterator(String name, String eventType, List<String> events) {}

  public FakeGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> ops =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());

    writer.addImport(RuntimeTypes.NSMITHY_CORE);

    String serviceTypeName = CSharpNaming.typeName(service.getId().getName());
    String contract =
        serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
    String aggInterface = "I" + contract + "Handler";
    String fakeClass = "Fake" + contract + "Handler";

    writer.writeXmlDocs(
        "Fake "
            + aggInterface
            + " returning canned responses: the output of each operation's first non-error"
            + " @examples entry when present, otherwise placeholder values synthesized from the"
            + " model. Responses are deterministic. Override an operation method in a subclass, or"
            + " register a real per-operation handler after this one, to replace individual"
            + " operations.",
        Map.of());
    writer.write("public class $L : $L", fakeClass, aggInterface);
    writer.openBlock(
        "{",
        "}",
        () -> {
          boolean first = true;
          for (OperationShape op : ops) {
            if (!first) {
              writer.write("");
            }
            first = false;
            writeOperationMethod(op);
          }
          for (PendingIterator iterator : pendingIterators) {
            writer.write("");
            writeIterator(iterator);
          }
        });
  }

  // ---------------- operation methods ----------------

  private void writeOperationMethod(OperationShape op) {
    Model model = context.model();
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    writer.write("public virtual $L", operationSignature(op));
    if (!hasOutput) {
      writer.openBlock(
          "{", "}", () -> writer.write("return System.Threading.Tasks.Task.CompletedTask;"));
      return;
    }

    StructureShape output = model.expectShape(op.getOutputShape(), StructureShape.class);
    ObjectNode example = firstExampleOutput(op);
    if (example == null) {
      LOGGER.warning(
          () ->
              "No non-error @examples output for "
                  + op.getId()
                  + "; the fake handler returns synthesized placeholder values.");
    }
    String expr = structureExpr(output, example, new LinkedHashSet<>(), op);
    if (expr == null) {
      throw new CodegenException(
          "Cannot synthesize a fake output for operation " + op.getId() + ".");
    }
    writer.openBlock(
        "{", "}", () -> writer.write("return System.Threading.Tasks.Task.FromResult($L);", expr));
  }

  private void writeIterator(PendingIterator iterator) {
    writer.write(
        "private static async System.Collections.Generic.IAsyncEnumerable<$L> $L()",
        iterator.eventType(),
        iterator.name());
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);");
          for (String event : iterator.events()) {
            writer.write("yield return $L;", event);
          }
        });
  }

  /** Same delegate shape the handler interfaces declare; see ServerGenerator. */
  private String operationSignature(OperationShape op) {
    Model model = context.model();
    SymbolProvider sp = context.symbolProvider();
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<"
                + CSharpSymbolProvider.qualified(
                    sp.toSymbol(model.expectShape(op.getOutputShape())))
                + ">"
            : "System.Threading.Tasks.Task";
    String params =
        hasInput
            ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())))
                + " input, "
            : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }

  private ObjectNode firstExampleOutput(OperationShape op) {
    return op.getTrait(ExamplesTrait.class).stream()
        .flatMap(trait -> trait.getExamples().stream())
        .filter(example -> example.getError().isEmpty())
        .map(example -> example.getOutput().orElse(null))
        .filter(Objects::nonNull)
        .findFirst()
        .orElse(null);
  }

  // ---------------- value synthesis ----------------

  /**
   * Structure construction uses named arguments so members absent from an example are simply
   * omitted regardless of their constructor position. `op` is non-null only for the operation's own
   * output structure, the one place event-stream and streaming-blob members can appear.
   */
  private String structureExpr(
      StructureShape shape, ObjectNode example, Set<ShapeId> stack, OperationShape op) {
    Model model = context.model();
    if (!stack.add(shape.getId())) {
      return null;
    }
    try {
      List<String> args = new ArrayList<>();
      for (MemberShape member : ShapeSupport.constructorMembers(shape)) {
        Node valueNode =
            example == null ? null : example.getMember(member.getMemberName()).orElse(null);
        if (valueNode != null && valueNode.isNullNode()) {
          valueNode = null;
        }
        if (example != null && valueNode == null && !ShapeSupport.isRequired(member)) {
          continue;
        }

        String expr;
        if (op != null && ShapeSupport.isEventStreamMember(model, member)) {
          expr = eventStreamExpr(op, member, valueNode, stack);
        } else if (op != null && ShapeSupport.isStreamingBlobMember(model, member)) {
          expr =
              "new System.IO.MemoryStream("
                  + blobBytesExpr(valueNode, CSharpNaming.camel(member.getMemberName()))
                  + ")";
        } else {
          expr = memberExpr(member, valueNode, stack);
        }

        if (expr == null) {
          if (ShapeSupport.isRequired(member)) {
            throw new CodegenException(
                "Cannot synthesize a fake value for required member "
                    + member.getId()
                    + ": the shape is recursive with no synthesizable alternative.");
          }
          continue;
        }
        args.add(CSharpNaming.propertyName(member.getMemberName()) + ": " + expr);
      }
      return "new " + qualifiedType(shape) + "(" + String.join(", ", args) + ")";
    } finally {
      stack.remove(shape.getId());
    }
  }

  private String memberExpr(MemberShape member, Node node, Set<ShapeId> stack) {
    Shape target = context.model().expectShape(member.getTarget());
    return shapeExpr(target, node, stack, CSharpNaming.camel(member.getMemberName()), member);
  }

  private String shapeExpr(
      Shape target, Node node, Set<ShapeId> stack, String hint, MemberShape member) {
    return switch (target.getType()) {
      case BOOLEAN ->
          node != null && node.isBooleanNode()
              ? (node.expectBooleanNode().getValue() ? "true" : "false")
              : "true";
      case STRING ->
          node != null && node.isStringNode()
              ? CSharpNaming.formatString(node.expectStringNode().getValue())
              : CSharpNaming.formatString(constrainedString(hint, member, target));
      case BLOB -> blobBytesExpr(node, hint);
      case BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE, BIG_INTEGER, BIG_DECIMAL ->
          numberExpr(target.getType(), node, member, target);
      case TIMESTAMP -> timestampExpr(node);
      case DOCUMENT ->
          node != null
              ? ShapeSupport.documentLiteral(node)
              : "NSmithy.Core.Document.From(" + CSharpNaming.formatString(hint) + ")";
      case ENUM -> enumExpr(target, node);
      case INT_ENUM -> intEnumExpr(target, node);
      case STRUCTURE ->
          ShapeSupport.isUnit(target.getId())
              ? "SmithyUnit.Value"
              : structureExpr(
                  target.asStructureShape().orElseThrow(),
                  node != null && node.isObjectNode() ? node.expectObjectNode() : null,
                  stack,
                  null);
      case UNION -> unionExpr(target.asUnionShape().orElseThrow(), node, stack);
      case LIST, SET -> listExpr(target, node, stack, hint, member);
      case MAP -> mapExpr(target.asMapShape().orElseThrow(), node, stack, member);
      default -> null;
    };
  }

  private String enumExpr(Shape target, Node node) {
    String typeName = qualifiedType(target);
    if (node != null && node.isStringNode()) {
      return "new "
          + typeName
          + "("
          + CSharpNaming.formatString(node.expectStringNode().getValue())
          + ")";
    }
    List<MemberShape> members = ShapeSupport.sortedMembers(target);
    return typeName + "." + CSharpNaming.propertyName(members.get(0).getMemberName());
  }

  private String intEnumExpr(Shape target, Node node) {
    String typeName = qualifiedType(target);
    if (node != null && node.isNumberNode()) {
      return "(" + typeName + ")" + node.expectNumberNode().getValue().longValue();
    }
    List<MemberShape> members = ShapeSupport.sortedMembers(target);
    return typeName + "." + CSharpNaming.propertyName(members.get(0).getMemberName());
  }

  private String unionExpr(UnionShape union, Node node, Set<ShapeId> stack) {
    String typeName = qualifiedType(union);
    if (node != null && node.isObjectNode()) {
      ObjectNode obj = node.expectObjectNode();
      for (Map.Entry<String, Node> entry : obj.getStringMap().entrySet()) {
        Optional<MemberShape> variant = union.getMember(entry.getKey());
        if (variant.isEmpty()) {
          continue;
        }
        String value = memberExpr(variant.get(), entry.getValue(), stack);
        if (value != null) {
          return typeName
              + ".From"
              + CSharpNaming.typeName(variant.get().getMemberName())
              + "("
              + value
              + ")";
        }
      }
    }
    for (MemberShape variant : ShapeSupport.sortedMembers(union)) {
      String value = memberExpr(variant, null, stack);
      if (value != null) {
        return typeName
            + ".From"
            + CSharpNaming.typeName(variant.getMemberName())
            + "("
            + value
            + ")";
      }
    }
    return null;
  }

  private String listExpr(
      Shape target, Node node, Set<ShapeId> stack, String hint, MemberShape member) {
    Model model = context.model();
    MemberShape elementMember =
        target.getType() == ShapeType.LIST
            ? target.asListShape().map(ListShape::getMember).orElseThrow()
            : target.asSetShape().orElseThrow().getMember();
    Shape elementTarget = model.expectShape(elementMember.getTarget());
    String elementType =
        CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(elementTarget))
            + (ShapeSupport.isSparse(target) ? "?" : "");

    List<String> elements = new ArrayList<>();
    if (node != null && node.isArrayNode()) {
      for (Node element : node.expectArrayNode().getElements()) {
        String expr =
            element.isNullNode()
                ? "null"
                : shapeExpr(elementTarget, element, stack, hint, elementMember);
        if (expr == null) {
          return null;
        }
        elements.add(expr);
      }
    } else {
      long count = Math.max(1, lengthMin(member, target));
      String expr = shapeExpr(elementTarget, null, stack, hint, elementMember);
      if (expr != null) {
        for (long i = 0; i < count; i++) {
          elements.add(expr);
        }
      }
    }

    String typeName = qualifiedType(target);
    if (elements.isEmpty()) {
      return "new " + typeName + "(System.Array.Empty<" + elementType + ">())";
    }
    return "new "
        + typeName
        + "(new "
        + elementType
        + "[] { "
        + String.join(", ", elements)
        + " })";
  }

  private String mapExpr(MapShape map, Node node, Set<ShapeId> stack, MemberShape member) {
    Model model = context.model();
    Shape keyTarget = model.expectShape(map.getKey().getTarget());
    Shape valueTarget = model.expectShape(map.getValue().getTarget());
    String valueType =
        CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(valueTarget))
            + (ShapeSupport.isSparse(map) ? "?" : "");

    List<String> entries = new ArrayList<>();
    if (node != null && node.isObjectNode()) {
      for (Map.Entry<String, Node> entry : node.expectObjectNode().getStringMap().entrySet()) {
        String value =
            entry.getValue().isNullNode()
                ? "null"
                : shapeExpr(valueTarget, entry.getValue(), stack, "value", map.getValue());
        if (value == null) {
          return null;
        }
        entries.add("{ " + CSharpNaming.formatString(entry.getKey()) + ", " + value + " }");
      }
    } else {
      long count = Math.max(1, lengthMin(member, map));
      String value = shapeExpr(valueTarget, null, stack, "value", map.getValue());
      if (value != null) {
        for (long i = 0; i < count; i++) {
          String key = constrainedString(i == 0 ? "key" : "key" + i, map.getKey(), keyTarget);
          entries.add("{ " + CSharpNaming.formatString(key) + ", " + value + " }");
        }
      }
    }

    String typeName = qualifiedType(map);
    String dictionary = "System.Collections.Generic.Dictionary<string, " + valueType + ">";
    if (entries.isEmpty()) {
      return "new " + typeName + "(new " + dictionary + "())";
    }
    return "new " + typeName + "(new " + dictionary + " { " + String.join(", ", entries) + " })";
  }

  private String eventStreamExpr(
      OperationShape op, MemberShape member, Node node, Set<ShapeId> stack) {
    Model model = context.model();
    Shape union = model.expectShape(member.getTarget());
    String eventType = qualifiedType(union);

    List<String> events = new ArrayList<>();
    if (node != null && node.isArrayNode()) {
      for (Node element : node.expectArrayNode().getElements()) {
        String expr =
            shapeExpr(union, element, stack, CSharpNaming.camel(member.getMemberName()), member);
        if (expr != null) {
          events.add(expr);
        }
      }
    }
    if (events.isEmpty()) {
      String expr =
          shapeExpr(union, null, stack, CSharpNaming.camel(member.getMemberName()), member);
      if (expr != null) {
        events.add(expr);
      }
    }
    if (events.isEmpty()) {
      return null;
    }

    String name =
        "Fake"
            + CSharpNaming.typeName(op.getId().getName())
            + CSharpNaming.propertyName(member.getMemberName())
            + "Events";
    pendingIterators.add(new PendingIterator(name, eventType, events));
    return name + "()";
  }

  private String blobBytesExpr(Node node, String hint) {
    if (node != null && node.isStringNode()) {
      return "System.Convert.FromBase64String("
          + CSharpNaming.formatString(node.expectStringNode().getValue())
          + ")";
    }
    return "System.Text.Encoding.UTF8.GetBytes(" + CSharpNaming.formatString(hint) + ")";
  }

  private String timestampExpr(Node node) {
    if (node != null && node.isNumberNode()) {
      return "System.DateTimeOffset.FromUnixTimeSeconds("
          + node.expectNumberNode().getValue().longValue()
          + ")";
    }
    if (node != null && node.isStringNode()) {
      return "System.DateTimeOffset.Parse("
          + CSharpNaming.formatString(node.expectStringNode().getValue())
          + ", System.Globalization.CultureInfo.InvariantCulture)";
    }
    return "System.DateTimeOffset.FromUnixTimeSeconds(" + PLACEHOLDER_EPOCH_SECONDS + ")";
  }

  private String numberExpr(ShapeType type, Node node, MemberShape member, Shape target) {
    if (node != null
        && node.isStringNode()
        && (type == ShapeType.FLOAT || type == ShapeType.DOUBLE)) {
      String csType = type == ShapeType.FLOAT ? "float" : "double";
      return switch (node.expectStringNode().getValue()) {
        case "NaN" -> csType + ".NaN";
        case "Infinity" -> csType + ".PositiveInfinity";
        case "-Infinity" -> csType + ".NegativeInfinity";
        default -> null;
      };
    }

    BigDecimal value;
    if (node != null && node.isNumberNode()) {
      value = new BigDecimal(node.expectNumberNode().getValue().toString());
    } else {
      value = placeholderNumber(member, target);
    }
    return switch (type) {
      case BYTE, SHORT, INTEGER -> Long.toString(value.longValue());
      case LONG -> value.longValue() + "L";
      case FLOAT -> value.floatValue() + "f";
      case DOUBLE -> value.doubleValue() + "d";
      case BIG_INTEGER -> "System.Numerics.BigInteger.Parse(\"" + value.toBigInteger() + "\")";
      case BIG_DECIMAL -> value.toPlainString() + "m";
      default -> throw new CodegenException("Not a numeric shape type: " + type);
    };
  }

  private BigDecimal placeholderNumber(MemberShape member, Shape target) {
    if (member != null && member.hasTrait(HttpResponseCodeTrait.class)) {
      return BigDecimal.valueOf(200);
    }
    RangeTrait range = trait(RangeTrait.class, member, target);
    BigDecimal value = BigDecimal.ZERO;
    if (range != null) {
      if (range.getMin().isPresent() && value.compareTo(range.getMin().get()) < 0) {
        value = range.getMin().get();
      }
      if (range.getMax().isPresent() && value.compareTo(range.getMax().get()) > 0) {
        value = range.getMax().get();
      }
    }
    return value;
  }

  private String constrainedString(String hint, MemberShape member, Shape target) {
    LengthTrait length = trait(LengthTrait.class, member, target);
    String value = hint;
    if (length != null) {
      long min = length.getMin().orElse(0L);
      if (value.length() < min) {
        value = value + "x".repeat((int) (min - value.length()));
      }
      if (length.getMax().isPresent() && value.length() > length.getMax().get()) {
        value = value.substring(0, length.getMax().get().intValue());
      }
    }
    return value;
  }

  private long lengthMin(MemberShape member, Shape target) {
    LengthTrait length = trait(LengthTrait.class, member, target);
    return length == null ? 0 : length.getMin().orElse(0L);
  }

  private <T extends Trait> T trait(
      Class<T> traitClass, MemberShape member, Shape target) {
    if (member != null && member.hasTrait(traitClass)) {
      return member.expectTrait(traitClass);
    }
    return target.getTrait(traitClass).orElse(null);
  }

  private String qualifiedType(Shape shape) {
    return CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(shape));
  }
}

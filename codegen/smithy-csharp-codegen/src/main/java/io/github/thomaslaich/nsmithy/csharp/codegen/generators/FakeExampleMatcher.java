/*
 * Example input matching for the generated fakes. When an operation has more than one @examples
 * entry, or any error example, the fake picks its response by matching the incoming input against
 * each example's input in model order; the first match wins. Matching is a subset comparison at
 * every nesting level: members present in the example input must equal the corresponding input
 * property, members absent from the example are wildcards. A matched non-error example returns
 * that example's output, a matched error example throws the modeled error, and when nothing
 * matches the fake falls back to the canned response FakeValueSynthesizer produces. Operations
 * with a single non-error example generate no matching code at all, so their responses stay
 * exactly as before.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.logging.Logger;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.node.ArrayNode;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.ListShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.shapes.UnionShape;
import software.amazon.smithy.model.traits.ExamplesTrait;

final class FakeExampleMatcher {

  private static final Logger LOGGER = Logger.getLogger(FakeExampleMatcher.class.getName());

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final FakeValueSynthesizer values;
  private final List<PendingMatcher> pendingMatchers = new ArrayList<>();

  private record PendingMatcher(
      String name, String title, String inputType, List<String> conditions) {}

  private record Arm(String matchMethod, String title, String statement) {}

  FakeExampleMatcher(GenerationContext context, CSharpWriter writer, FakeValueSynthesizer values) {
    this.context = context;
    this.writer = writer;
    this.values = values;
  }

  /**
   * Writes the statements of a fake operation method: one match arm per @examples entry in model
   * order, then the unconditional fallback return.
   */
  void writeOperationBody(CSharpWriter writer, OperationShape op) {
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    for (Arm arm : arms(op)) {
      writer.write("// $L", quote(arm.title()));
      writer.write("if ($L(input))", arm.matchMethod());
      writer.openBlock("{", "}", () -> writer.write("$L", arm.statement()));
    }
    if (hasOutput) {
      writer.write(
          "return " + writer.frameworkType("System.Threading.Tasks.Task") + ".FromResult($L);",
          values.outputExpr(op));
    } else {
      writer.write(
          "return " + writer.frameworkType("System.Threading.Tasks.Task") + ".CompletedTask;");
    }
  }

  /** Writes the private match-condition methods recorded while writing operation bodies. */
  void writePendingMatchers(CSharpWriter writer) {
    for (PendingMatcher matcher : pendingMatchers) {
      writer.write("");
      writer.write(
          "// True when the input matches the $L @examples entry.", quote(matcher.title()));
      writer.write("private static bool $L($L input)", matcher.name(), matcher.inputType());
      writer.openBlock(
          "{",
          "}",
          () -> {
            for (String condition : matcher.conditions()) {
              writer.write("if (!($L))", condition);
              writer.openBlock("{", "}", () -> writer.write("return false;"));
            }
            writer.write("return true;");
          });
    }
  }

  private List<Arm> arms(OperationShape op) {
    List<ExamplesTrait.Example> examples =
        op.getTrait(ExamplesTrait.class).map(ExamplesTrait::getExamples).orElse(List.of());
    if (examples.isEmpty()) {
      return List.of();
    }
    // A single non-error example is already the unconditional fallback response.
    if (examples.size() == 1 && examples.get(0).getError().isEmpty()) {
      return List.of();
    }
    if (ShapeSupport.isUnit(op.getInputShape())) {
      LOGGER.warning(
          () ->
              op.getId()
                  + " has multiple or error @examples but no input to match; the "
                  + values.subject()
                  + " always returns the fallback response.");
      return List.of();
    }

    Model model = context.model();
    StructureShape input = model.expectShape(op.getInputShape(), StructureShape.class);
    String inputType = values.qualifiedType(input);
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String outputType =
        hasOutput ? values.qualifiedType(model.expectShape(op.getOutputShape())) : null;

    List<Arm> arms = new ArrayList<>();
    for (int i = 0; i < examples.size(); i++) {
      ExamplesTrait.Example example = examples.get(i);
      String name = "Matches" + CSharpNaming.typeName(op.getId().getName()) + "Example" + i;
      List<String> conditions = new ArrayList<>();
      structureConditions("input", input, example.getInput(), conditions, new int[] {0});
      pendingMatchers.add(new PendingMatcher(name, example.getTitle(), inputType, conditions));

      String statement;
      Optional<ExamplesTrait.ErrorExample> error = example.getError();
      if (error.isPresent()) {
        String errorExpr = values.errorExpr(error.get().getShapeId(), error.get().getContent());
        statement =
            hasOutput
                ? "return "
                    + writer.frameworkType("System.Threading.Tasks.Task")
                    + ".FromException<"
                    + outputType
                    + ">("
                    + errorExpr
                    + ");"
                : "return "
                    + writer.frameworkType("System.Threading.Tasks.Task")
                    + ".FromException("
                    + errorExpr
                    + ");";
      } else if (hasOutput) {
        ObjectNode output = example.getOutput().orElse(Node.objectNode());
        statement =
            "return "
                + writer.frameworkType("System.Threading.Tasks.Task")
                + ".FromResult("
                + values.outputExprFor(op, output)
                + ");";
      } else {
        statement =
            "return " + writer.frameworkType("System.Threading.Tasks.Task") + ".CompletedTask;";
      }
      arms.add(new Arm(name, example.getTitle(), statement));
    }
    return arms;
  }

  // ---------------- condition generation ----------------

  /** Adds one condition per member present in the example node; absent members are wildcards. */
  private void structureConditions(
      String subject, StructureShape shape, ObjectNode node, List<String> out, int[] counter) {
    Model model = context.model();
    for (Map.Entry<String, Node> entry : node.getStringMap().entrySet()) {
      MemberShape member = shape.getMember(entry.getKey()).orElse(null);
      if (member == null) {
        LOGGER.warning(
            () ->
                "Example input member "
                    + entry.getKey()
                    + " does not exist on "
                    + shape.getId()
                    + "; the "
                    + values.subject()
                    + " ignores it when matching.");
        continue;
      }
      Node valueNode = entry.getValue();
      if (valueNode.isNullNode()) {
        continue;
      }
      if (ShapeSupport.isEventStreamMember(model, member)
          || ShapeSupport.isStreamingBlobMember(model, member)) {
        LOGGER.warning(
            () ->
                "Example input member "
                    + member.getId()
                    + " is a streaming member; the "
                    + values.subject()
                    + " ignores it when matching.");
        continue;
      }
      valueConditions(
          subject + "." + CSharpNaming.propertyName(member.getMemberName()),
          model.expectShape(member.getTarget()),
          valueNode,
          out,
          counter);
    }
  }

  private void valueConditions(
      String expr, Shape target, Node node, List<String> out, int[] counter) {
    Model model = context.model();
    switch (target.getType()) {
      case BOOLEAN ->
          out.add(expr + " == " + (node.expectBooleanNode().getValue() ? "true" : "false"));
      case STRING ->
          out.add(expr + " == " + CSharpNaming.formatString(node.expectStringNode().getValue()));
      case ENUM -> out.add(expr + " == " + values.enumExpr(target, node));
      case INT_ENUM -> out.add(expr + " == " + values.intEnumExpr(target, node));
      case BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE, BIG_INTEGER, BIG_DECIMAL ->
          numberConditions(expr, target, node, out, counter);
      case TIMESTAMP -> out.add(expr + " == " + values.timestampExpr(node));
      case BLOB -> {
        String v = nextVar(counter);
        out.add(expr + " is { } " + v);
        out.add(
            writer.frameworkType("System.Linq.Enumerable")
                + ".SequenceEqual("
                + v
                + ", "
                + values.blobBytesExpr(node, "")
                + ")");
      }
      case DOCUMENT ->
          LOGGER.warning(
              () ->
                  "Example input value for document member at "
                      + expr
                      + " cannot be compared; the "
                      + values.subject()
                      + " treats it as always matching.");
      case STRUCTURE -> {
        if (ShapeSupport.isUnit(target.getId())) {
          return;
        }
        String v = nextVar(counter);
        out.add(expr + " is { } " + v);
        structureConditions(
            v, target.asStructureShape().orElseThrow(), node.expectObjectNode(), out, counter);
      }
      case UNION -> unionConditions(expr, target.asUnionShape().orElseThrow(), node, out, counter);
      case LIST, SET -> {
        MemberShape elementMember =
            target.getType() == ShapeType.LIST
                ? target.asListShape().map(ListShape::getMember).orElseThrow()
                : target.asSetShape().orElseThrow().getMember();
        Shape elementTarget = model.expectShape(elementMember.getTarget());
        ArrayNode array = node.expectArrayNode();
        String v = nextVar(counter);
        out.add(expr + " is { } " + v);
        out.add(v + ".Values.Count == " + array.size());
        List<Node> elements = array.getElements();
        for (int i = 0; i < elements.size(); i++) {
          Node element = elements.get(i);
          if (element.isNullNode()) {
            out.add(v + ".Values[" + i + "] is null");
          } else {
            valueConditions(v + ".Values[" + i + "]", elementTarget, element, out, counter);
          }
        }
      }
      case MAP -> {
        MapShape map = target.asMapShape().orElseThrow();
        Shape valueTarget = model.expectShape(map.getValue().getTarget());
        ObjectNode obj = node.expectObjectNode();
        String v = nextVar(counter);
        out.add(expr + " is { } " + v);
        out.add(v + ".Values.Count == " + obj.size());
        for (Map.Entry<String, Node> entry : obj.getStringMap().entrySet()) {
          String mv = nextVar(counter);
          out.add(
              v
                  + ".Values.TryGetValue("
                  + CSharpNaming.formatString(entry.getKey())
                  + ", out var "
                  + mv
                  + ")");
          if (entry.getValue().isNullNode()) {
            out.add(mv + " is null");
          } else {
            valueConditions(mv, valueTarget, entry.getValue(), out, counter);
          }
        }
      }
      default ->
          LOGGER.warning(
              () ->
                  "Example input value at "
                      + expr
                      + " has unsupported shape type "
                      + target.getType()
                      + "; the "
                      + values.subject()
                      + " treats it as always matching.");
    }
  }

  private void numberConditions(
      String expr, Shape target, Node node, List<String> out, int[] counter) {
    ShapeType type = target.getType();
    if (node.isStringNode() && (type == ShapeType.FLOAT || type == ShapeType.DOUBLE)) {
      String csType = type == ShapeType.FLOAT ? "float" : "double";
      switch (node.expectStringNode().getValue()) {
        case "NaN" -> {
          String v = nextVar(counter);
          out.add(expr + " is { } " + v + " && " + csType + ".IsNaN(" + v + ")");
        }
        case "Infinity" -> out.add(expr + " == " + csType + ".PositiveInfinity");
        case "-Infinity" -> out.add(expr + " == " + csType + ".NegativeInfinity");
        default -> out.add("false");
      }
      return;
    }
    out.add(expr + " == " + values.numberExpr(type, node, null, target));
  }

  private void unionConditions(
      String expr, UnionShape union, Node node, List<String> out, int[] counter) {
    ObjectNode obj = node.expectObjectNode();
    for (Map.Entry<String, Node> entry : obj.getStringMap().entrySet()) {
      Optional<MemberShape> variant = union.getMember(entry.getKey());
      if (variant.isEmpty()) {
        continue;
      }
      String variantType =
          values.qualifiedType(union) + "." + CSharpNaming.typeName(variant.get().getMemberName());
      String v = nextVar(counter);
      out.add(expr + " is " + variantType + " " + v);
      valueConditions(
          v + ".Value",
          context.model().expectShape(variant.get().getTarget()),
          entry.getValue(),
          out,
          counter);
      return;
    }
    LOGGER.warning(
        () ->
            "Example input value for union at "
                + expr
                + " names no known variant; the "
                + values.subject()
                + " treats it as always matching.");
  }

  private static String nextVar(int[] counter) {
    return "v" + counter[0]++;
  }

  private static String quote(String title) {
    return "\"" + title.replace('\n', ' ').replace('\r', ' ') + "\"";
  }
}

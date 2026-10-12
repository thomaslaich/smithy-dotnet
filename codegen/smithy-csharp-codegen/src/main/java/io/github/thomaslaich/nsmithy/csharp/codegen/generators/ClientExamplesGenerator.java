package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.ExamplesTrait;

/** Renders actual example data, without synthesizing values for omitted optional members. */
final class ClientExamplesGenerator {
  private final GenerationContext context;
  private final CSharpWriter writer;
  private final FakeValueSynthesizer literals;
  private final List<String> iterators = new ArrayList<>();

  ClientExamplesGenerator(GenerationContext context, CSharpWriter writer) {
    this.context = context;
    this.writer = writer;
    literals = new FakeValueSynthesizer(context, writer, "client documentation");
  }

  void write(OperationShape operation) {
    operation
        .getTrait(ExamplesTrait.class)
        .ifPresent(
            trait -> {
              for (var example : trait.getExamples()) {
                iterators.clear();
                boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
                boolean hasOutput = !ShapeSupport.isUnit(operation.getOutputShape());
                StringBuilder code = new StringBuilder();
                if (hasInput) {
                  code.append("var input = ")
                      .append(value(operation.getInputShape(), example.getInput()))
                      .append(";\n");
                }
                String call =
                    "await client."
                        + CSharpNaming.typeName(operation.getId().getName())
                        + "Async("
                        + (hasInput ? "input" : "")
                        + ");";
                if (example.getError().isPresent()) {
                  writeErrorExample(code, call, example.getError().get());
                } else {
                  code.append(hasOutput ? "var output = " : "").append(call);
                  if (hasOutput && example.getOutput().isPresent()) {
                    code.append("\n\n// Expected output:\nvar expectedOutput = ")
                        .append(value(operation.getOutputShape(), example.getOutput().get()))
                        .append(";");
                  }
                }
                for (String iterator : iterators) {
                  code.append("\n\n").append(iterator);
                }
                writer.writeXmlExample(
                    example.getTitle(), example.getDocumentation().orElse(null), code.toString());
              }
            });
  }

  private void writeErrorExample(
      StringBuilder code, String call, ExamplesTrait.ErrorExample example) {
    StructureShape error = context.model().expectShape(example.getShapeId(), StructureShape.class);
    MemberShape message = ShapeSupport.errorMessageMember(context.model(), error).orElse(null);
    // The sample documents properties without inspecting an exception instance, so omit the
    // catch variable to keep pasted samples free of unused-variable warnings.
    code.append("try\n{\n    ")
        .append(call)
        .append("\n}\ncatch (")
        .append(type(error))
        .append(")\n{");
    if (example.getContent().isEmpty()) {
      code.append("\n    // Expected exception; no example properties supplied.");
    }
    for (MemberShape member : ShapeSupport.sortedMembers(error)) {
      example
          .getContent()
          .getMember(member.getMemberName())
          .ifPresent(
              node -> {
                String property =
                    member.equals(message) ? "Message" : CSharpNaming.memberName(member);
                code.append("\n    // Expected ")
                    .append(property)
                    .append(": ")
                    .append(memberValue(member, node));
              });
    }
    code.append("\n}");
  }

  private String value(ShapeId id, Node node) {
    Shape shape = context.model().expectShape(id);
    if (shape.isDocumentShape()) {
      return ShapeSupport.documentLiteral(writer, node);
    }
    if (node.isNullNode()) {
      return "null";
    }
    return switch (shape.getType()) {
      case STRUCTURE ->
          ShapeSupport.isUnit(id)
              ? writer.typeName(RuntimeTypes.SMITHY_UNIT) + ".Value"
              : structure(shape.asStructureShape().orElseThrow(), node.expectObjectNode());
      case UNION -> {
        // Smithy accepts an example union with no variant or an unknown one; documentation shows
        // it as absent rather than failing the build.
        var entry = node.expectObjectNode().getStringMap().entrySet().stream().findFirst();
        var member = entry.flatMap(e -> shape.getMember(e.getKey()));
        if (member.isEmpty()) {
          yield "null";
        }
        yield type(shape)
            + "."
            + CSharpNaming.variantFactoryName(member.get())
            + "("
            + value(member.get().getTarget(), entry.get().getValue())
            + ")";
      }
      case LIST, SET -> {
        MemberShape member =
            shape.isListShape()
                ? shape.asListShape().orElseThrow().getMember()
                : shape.asSetShape().orElseThrow().getMember();
        List<String> elements = new ArrayList<>();
        for (Node element : node.expectArrayNode().getElements()) {
          elements.add(value(member.getTarget(), element));
        }
        yield "new " + elementType(shape, member) + "[] { " + String.join(", ", elements) + " }";
      }
      case MAP -> {
        var map = shape.asMapShape().orElseThrow();
        List<String> entries = new ArrayList<>();
        node.expectObjectNode().getStringMap().entrySet().stream()
            .sorted(java.util.Map.Entry.comparingByKey())
            .forEach(
                entry ->
                    entries.add(
                        "{ "
                            + CSharpNaming.formatString(entry.getKey())
                            + ", "
                            + value(map.getValue().getTarget(), entry.getValue())
                            + " }"));
        yield "new "
            + writer.typeName(RuntimeTypes.DICTIONARY)
            + "<string, "
            + elementType(shape, map.getValue())
            + "> { "
            + String.join(", ", entries)
            + " }";
      }
      case STRING -> CSharpNaming.formatString(node.expectStringNode().getValue());
      case BOOLEAN -> Boolean.toString(node.expectBooleanNode().getValue());
      case ENUM -> literals.enumExpr(shape, node);
      case INT_ENUM -> literals.intEnumExpr(shape, node);
      case BLOB -> literals.blobBytesExpr(node, "");
      case TIMESTAMP -> {
        // Epoch seconds may have a fractional part; preserve it at DateTimeOffset precision.
        if (node.isNumberNode()) {
          var ticks =
              new BigDecimal(node.expectNumberNode().getValue().toString())
                  .movePointRight(7)
                  .toBigInteger();
          yield writer.typeName(RuntimeTypes.DATE_TIME_OFFSET)
              + ".UnixEpoch.AddTicks("
              + ticks
              + "L)";
        }
        yield literals.timestampExpr(node);
      }
      case BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE, BIG_INTEGER, BIG_DECIMAL ->
          literals.numberExpr(shape.getType(), node, null, shape);
      default -> throw new CodegenException("Unsupported example shape: " + id);
    };
  }

  private String structure(StructureShape shape, ObjectNode node) {
    boolean error = shape.hasTrait(ErrorTrait.class);
    MemberShape message =
        error ? ShapeSupport.errorMessageMember(context.model(), shape).orElse(null) : null;
    List<String> args = new ArrayList<>();
    if (error) {
      args.add(
          "message: "
              + (message == null
                  ? "null"
                  : node.getMember(message.getMemberName())
                      .map(n -> value(message.getTarget(), n))
                      .orElse("null")));
    }
    for (MemberShape member : ShapeSupport.constructorMembers(shape, message)) {
      Node memberNode = node.getMember(member.getMemberName()).orElse(null);
      if (memberNode == null && ShapeSupport.isOptionalParameter(member)) {
        continue;
      }
      // Error examples can intentionally omit required values. Do not invent sample data.
      String expression =
          memberNode == null
              ? "default! /* omitted in example */"
              : memberValue(member, memberNode);
      args.add(
          (error
                  ? CSharpNaming.parameterName(member.getMemberName())
                  : CSharpNaming.memberName(member))
              + ": "
              + expression);
    }
    return "new " + type(shape) + "(" + String.join(", ", args) + ")";
  }

  private String memberValue(MemberShape member, Node node) {
    if (!node.isNullNode() && ShapeSupport.isStreamingBlobMember(context.model(), member)) {
      return "new "
          + writer.typeName(RuntimeTypes.MEMORY_STREAM)
          + "("
          + literals.blobBytesExpr(node, "")
          + ")";
    }
    if (!node.isNullNode() && ShapeSupport.isEventStreamMember(context.model(), member)) {
      String name = "ExampleEvents" + (iterators.size() + 1);
      StringBuilder iterator =
          new StringBuilder("static async ")
              .append(writer.typeName(RuntimeTypes.I_ASYNC_ENUMERABLE))
              .append("<")
              .append(type(context.model().expectShape(member.getTarget())))
              .append("> ")
              .append(name)
              .append("()\n{\n    await ")
              .append(writer.typeName(RuntimeTypes.TASK))
              .append(".CompletedTask;\n");
      for (Node event : node.isArrayNode() ? node.expectArrayNode().getElements() : List.of(node)) {
        iterator.append("    yield return ").append(value(member.getTarget(), event)).append(";\n");
      }
      iterator.append("    yield break;\n}");
      iterators.add(iterator.toString());
      return name + "()";
    }
    return value(member.getTarget(), node);
  }

  private String elementType(Shape collection, MemberShape member) {
    return type(context.model().expectShape(member.getTarget()))
        + (ShapeSupport.isSparse(collection) ? "?" : "");
  }

  private String type(Shape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape));
  }
}

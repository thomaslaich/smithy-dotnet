/*
 * Compiles the JMESPath subset of @operationContextParams into C# over a generated input.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.jmespath.JmespathExpression;
import software.amazon.smithy.jmespath.ast.CurrentExpression;
import software.amazon.smithy.jmespath.ast.FieldExpression;
import software.amazon.smithy.jmespath.ast.FlattenExpression;
import software.amazon.smithy.jmespath.ast.FunctionExpression;
import software.amazon.smithy.jmespath.ast.MultiSelectListExpression;
import software.amazon.smithy.jmespath.ast.ProjectionExpression;
import software.amazon.smithy.jmespath.ast.Subexpression;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.EnumTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders an {@code @operationContextParams} path as a C# expression over the operation input.
 * Supports the subset the rules engine allows: identifiers, sub-expressions, wildcard list
 * projections, multi-select lists, flattening, and {@code keys}. Projections drop null results, as
 * JMESPath does.
 */
@SmithyInternalApi
final class OperationContextPathRenderer {

  private static final ShapeId STRING = ShapeId.from("smithy.api#String");

  private sealed interface Value permits Scalar, Sequence, Tuple {}

  /** A single value of a shape; nullable when any step to it may be absent. */
  private record Scalar(String code, Shape shape, boolean nullable) implements Value {}

  /** A non-null {@code IEnumerable} of non-null items, or of tuples when {@code tuples}. */
  private record Sequence(String code, Shape item, boolean tuples) implements Value {}

  /** A multi-select list of values. */
  private record Tuple(List<Scalar> items) implements Value {}

  /** A rendered path: the C# expression and whether it may be null. */
  record Path(String code, boolean nullable) {}

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final Model model;
  private int lambdas;

  OperationContextPathRenderer(GenerationContext context, CSharpWriter writer) {
    this.context = context;
    this.writer = writer;
    this.model = context.model();
  }

  /**
   * Renders {@code path} over the C# local {@code input}, which holds the operation input, for a
   * parameter of the given kind.
   */
  Path render(String path, Shape input, EndpointValueType.Kind kind) {
    writer.addImport("System.Linq");
    Value value = translate(JmespathExpression.parse(path), new Scalar("input", input, false));
    if (kind == EndpointValueType.Kind.STRING_LIST) {
      if (value instanceof Sequence sequence) {
        return new Path(sequence.code() + ".ToArray()", false);
      }
      if (value instanceof Scalar scalar && scalar.shape().isListShape()) {
        return new Path(scalar.code(), scalar.nullable());
      }
    } else if (value instanceof Scalar scalar) {
      boolean matches =
          kind == EndpointValueType.Kind.BOOLEAN
              ? scalar.shape().getType() == ShapeType.BOOLEAN
              : scalar.shape().getType() == ShapeType.STRING;
      if (matches) {
        return new Path(scalar.code(), scalar.nullable());
      }
    }
    throw new CodegenException(
        "The @operationContextParams path `" + path + "` does not produce a " + kind + " value.");
  }

  private Value translate(JmespathExpression expression, Scalar current) {
    if (expression instanceof CurrentExpression) {
      return current;
    }
    if (expression instanceof FieldExpression field) {
      return member(current, field.getName());
    }
    if (expression instanceof Subexpression subexpression) {
      return translate(
          subexpression.getRight(), scalar(translate(subexpression.getLeft(), current)));
    }
    if (expression instanceof FunctionExpression function
        && function.getName().equals("keys")
        && function.getArguments().size() == 1) {
      Scalar map = scalar(translate(function.getArguments().get(0), current));
      if (!map.shape().isMapShape()) {
        throw unsupported(expression);
      }
      String keys =
          map.nullable()
              ? "(" + map.code() + "?.Keys ?? " + empty("string") + ")"
              : map.code() + ".Keys";
      return new Sequence(keys, model.expectShape(STRING), false);
    }
    if (expression instanceof MultiSelectListExpression multiSelect) {
      List<Scalar> items = new ArrayList<>();
      for (JmespathExpression item : multiSelect.getExpressions()) {
        items.add(scalar(translate(item, current)));
      }
      return new Tuple(items);
    }
    if (expression instanceof FlattenExpression flatten) {
      Value inner = translate(flatten.getExpression(), current);
      if (inner instanceof Sequence sequence && sequence.tuples()) {
        return new Sequence(
            sequence.code()
                + ".SelectMany(static items => items).OfType<"
                + typeName(sequence.item())
                + ">()",
            sequence.item(),
            false);
      }
      throw unsupported(expression);
    }
    if (expression instanceof ProjectionExpression projection) {
      Sequence items = sequence(translate(projection.getLeft(), current), expression);
      if (projection.getRight() instanceof CurrentExpression) {
        return items;
      }
      String parameter = "item" + ++lambdas;
      Value mapped = translate(projection.getRight(), new Scalar(parameter, items.item(), false));
      if (mapped instanceof Scalar scalar) {
        return new Sequence(
            items.code()
                + ".Select("
                + parameter
                + " => "
                + scalar.code()
                + ").OfType<"
                + typeName(scalar.shape())
                + ">()",
            scalar.shape(),
            false);
      }
      if (mapped instanceof Tuple tuple) {
        Shape item = tuple.items().get(0).shape();
        String array =
            "new "
                + typeName(item)
                + "?[] { "
                + tuple.items().stream().map(Scalar::code).collect(Collectors.joining(", "))
                + " }";
        return new Sequence(
            items.code() + ".Select(" + parameter + " => " + array + ")", item, true);
      }
      throw unsupported(expression);
    }
    throw unsupported(expression);
  }

  private Scalar member(Scalar current, String name) {
    Shape container = current.shape();
    if (!container.isStructureShape() && !container.isUnionShape()) {
      throw new CodegenException(
          "The @operationContextParams field `" + name + "` is read from a non-structure.");
    }
    MemberShape member =
        container
            .getMember(name)
            .orElseThrow(
                () -> new CodegenException(container.getId() + " has no member `" + name + "`."));
    Shape target = model.expectShape(member.getTarget());
    boolean nullable;
    String code;
    if (container.isUnionShape()) {
      // A union member is set only when it is the union's variant.
      String variant =
          writer.typeName(context.symbolProvider().toSymbol(container))
              + "."
              + CSharpNaming.variantName(member);
      code = "(" + current.code() + " as " + variant + ")?.Value";
      nullable = true;
    } else {
      nullable = current.nullable() || ShapeSupport.isNullable(member);
      code = current.code() + (current.nullable() ? "?." : ".") + CSharpNaming.memberName(member);
    }
    if (target.getType() == ShapeType.ENUM || target.hasTrait(EnumTrait.class)) {
      code += nullable ? "?.Value" : ".Value";
      target = model.expectShape(STRING);
    }
    return new Scalar(code, target, nullable);
  }

  private Sequence sequence(Value value, JmespathExpression expression) {
    if (value instanceof Sequence sequence) {
      return sequence;
    }
    if (value instanceof Scalar scalar && scalar.shape().isListShape()) {
      Shape item = model.expectShape(scalar.shape().asListShape().get().getMember().getTarget());
      String code =
          scalar.nullable()
              ? "(" + scalar.code() + " ?? " + empty(typeName(item)) + ")"
              : scalar.code();
      return new Sequence(code, item, false);
    }
    throw unsupported(expression);
  }

  private Scalar scalar(Value value) {
    if (value instanceof Scalar scalar) {
      return scalar;
    }
    throw new CodegenException("@operationContextParams paths cannot read a field of a list.");
  }

  private String empty(String type) {
    return writer.typeName(RuntimeTypes.ENUMERABLE) + ".Empty<" + type + ">()";
  }

  private String typeName(Shape shape) {
    return switch (shape.getType()) {
      case STRING -> "string";
      case BOOLEAN -> "bool";
      default -> writer.typeName(context.symbolProvider().toSymbol(shape));
    };
  }

  private static CodegenException unsupported(JmespathExpression expression) {
    return new CodegenException("Unsupported @operationContextParams expression: " + expression);
  }
}

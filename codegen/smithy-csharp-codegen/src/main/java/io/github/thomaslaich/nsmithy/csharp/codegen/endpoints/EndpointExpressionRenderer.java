/*
 * Renders rules engine expressions as C# expressions over a generated resolver's locals.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.rulesengine.language.evaluation.type.OptionalType;
import software.amazon.smithy.rulesengine.language.syntax.Identifier;
import software.amazon.smithy.rulesengine.language.syntax.expressions.Expression;
import software.amazon.smithy.rulesengine.language.syntax.expressions.ExpressionVisitor;
import software.amazon.smithy.rulesengine.language.syntax.expressions.Reference;
import software.amazon.smithy.rulesengine.language.syntax.expressions.Template;
import software.amazon.smithy.rulesengine.language.syntax.expressions.functions.FunctionDefinition;
import software.amazon.smithy.rulesengine.language.syntax.expressions.functions.GetAttr;
import software.amazon.smithy.rulesengine.language.syntax.expressions.literal.Literal;
import software.amazon.smithy.rulesengine.language.syntax.expressions.literal.LiteralVisitor;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders rules engine expressions as C#. Every rendered expression records its C# type and whether
 * C# holds it as nullable, which is how "empty" rules values are represented.
 *
 * <p>The rules type checker narrows a reference to non-optional once a guarding {@code isSet} or
 * {@code assign} has proven it set. The BDD only reaches such a use on paths where that holds, so
 * the renderer asserts the C# local non-null there ({@code !} or {@code .Value}).
 */
@SmithyInternalApi
final class EndpointExpressionRenderer
    implements ExpressionVisitor<EndpointExpressionRenderer.Rendered> {

  private static final Pattern SIMPLE = Pattern.compile("[A-Za-z_@][A-Za-z0-9_.!]*");

  /** A rendered C# expression. */
  record Rendered(String code, EndpointValueType type, boolean nullable) {}

  /** A C# local holding a rule parameter or an assigned condition value. */
  record Local(String name, EndpointValueType type, boolean nullable) {}

  private final CSharpWriter writer;
  private final EndpointRules rules;
  private final Map<Identifier, Local> locals;

  EndpointExpressionRenderer(
      CSharpWriter writer, EndpointRules rules, Map<Identifier, Local> locals) {
    this.writer = writer;
    this.rules = rules;
    this.locals = locals;
  }

  Rendered render(Expression expression) {
    return expression.accept(this);
  }

  /** Renders an expression the rules type checker has proven set, as a non-nullable C# value. */
  String renderValue(Expression expression) {
    return nonNull(render(expression));
  }

  static String nonNull(Rendered rendered) {
    if (!rendered.nullable()) {
      return rendered.code();
    }
    String operand = parenthesize(rendered.code());
    return rendered.type().isValueType() ? operand + ".Value" : operand + "!";
  }

  static String parenthesize(String code) {
    return SIMPLE.matcher(code).matches() ? code : "(" + code + ")";
  }

  @Override
  public Rendered visitLiteral(Literal literal) {
    return literal.accept(
        new LiteralVisitor<Rendered>() {
          @Override
          public Rendered visitBoolean(boolean value) {
            return new Rendered(Boolean.toString(value), EndpointValueType.BOOLEAN, false);
          }

          @Override
          public Rendered visitString(Template value) {
            return new Rendered(template(value), EndpointValueType.STRING, false);
          }

          @Override
          public Rendered visitInteger(int value) {
            return new Rendered(Integer.toString(value), EndpointValueType.INTEGER, false);
          }

          @Override
          public Rendered visitRecord(Map<Identifier, Literal> members) {
            throw new CodegenException(
                "Record literals are only supported in endpoint properties: " + literal);
          }

          @Override
          public Rendered visitTuple(List<Literal> members) {
            throw new CodegenException(
                "Tuple literals are only supported in endpoint properties: " + literal);
          }
        });
  }

  @Override
  public Rendered visitRef(Reference reference) {
    Local local = local(reference.getName());
    boolean optional = reference.type() instanceof OptionalType;
    Rendered raw = new Rendered(local.name(), local.type(), local.nullable());
    return optional ? raw : new Rendered(nonNull(raw), local.type(), false);
  }

  @Override
  public Rendered visitGetAttr(GetAttr getAttr) {
    Rendered current = render(getAttr.getTarget());
    for (GetAttr.Part part : getAttr.getPath()) {
      if (part instanceof GetAttr.Part.Key key) {
        EndpointValueType.Field field = field(current.type(), key.key().getName().getValue());
        String access = current.nullable() ? "?." : ".";
        current =
            new Rendered(
                parenthesize(current.code()) + access + field.property(),
                field.type(),
                current.nullable() || field.nullable());
      } else if (part instanceof GetAttr.Part.Index index) {
        if (current.type().kind() != EndpointValueType.Kind.STRING_LIST) {
          throw new CodegenException("Endpoint rules index a non-list value: " + getAttr);
        }
        current =
            new Rendered(
                writer.format(
                    "$T.Index($L, $L)",
                    RuntimeTypes.ENDPOINT_FUNCTIONS,
                    current.code(),
                    index.index()),
                EndpointValueType.STRING,
                true);
      }
    }
    return getAttr.type() instanceof OptionalType
        ? current
        : new Rendered(nonNull(current), current.type(), false);
  }

  @Override
  public Rendered visitIsSet(Expression target) {
    // A reference is tested on its C# local: the type checker may already consider it set where
    // an earlier condition proved it, but this condition is the one doing the proving.
    Rendered value =
        target instanceof Reference reference ? rawLocal(reference.getName()) : render(target);
    String code = value.nullable() ? parenthesize(value.code()) + " is not null" : "true";
    return new Rendered(code, EndpointValueType.BOOLEAN, false);
  }

  @Override
  public Rendered visitNot(Expression target) {
    return new Rendered("!" + parenthesize(renderValue(target)), EndpointValueType.BOOLEAN, false);
  }

  @Override
  public Rendered visitBoolEquals(Expression left, Expression right) {
    return equality(left, right);
  }

  @Override
  public Rendered visitStringEquals(Expression left, Expression right) {
    return equality(left, right);
  }

  @Override
  public Rendered visitCoalesce(List<Expression> expressions) {
    List<Rendered> operands = new ArrayList<>();
    for (Expression expression : expressions) {
      Rendered operand = render(expression);
      operands.add(operand);
      if (!operand.nullable()) {
        break;
      }
    }
    Rendered last = operands.get(operands.size() - 1);
    if (operands.size() == 1) {
      return last;
    }
    String code =
        operands.stream().map(o -> parenthesize(o.code())).collect(Collectors.joining(" ?? "));
    return new Rendered(code, last.type(), last.nullable());
  }

  @Override
  public Rendered visitIte(Expression condition, Expression whenTrue, Expression whenFalse) {
    Rendered first = render(whenTrue);
    Rendered second = render(whenFalse);
    return new Rendered(
        parenthesize(renderValue(condition))
            + " ? "
            + parenthesize(first.code())
            + " : "
            + parenthesize(second.code()),
        first.type(),
        first.nullable() || second.nullable());
  }

  @Override
  public Rendered visitLibraryFunction(FunctionDefinition definition, List<Expression> arguments) {
    EndpointFunction function = rules.function(definition.getId());
    String args = arguments.stream().map(this::renderValue).collect(Collectors.joining(", "));
    return new Rendered(
        writer.format("$T.$L($L)", function.owner(), function.method(), args),
        function.returnType(),
        function.nullable());
  }

  /** A string template as a C# string literal or interpolated string. */
  String template(Template template) {
    List<Template.Part> parts = template.getParts();
    if (template.isStatic()) {
      return CSharpNaming.formatString(template.expectLiteral());
    }
    if (parts.size() == 1 && parts.get(0) instanceof Template.Dynamic dynamic) {
      return renderValue(dynamic.toExpression());
    }
    StringBuilder sb = new StringBuilder("$\"");
    for (Template.Part part : parts) {
      if (part instanceof Template.Literal literal) {
        String quoted = CSharpNaming.formatString(literal.getValue());
        sb.append(quoted.substring(1, quoted.length() - 1).replace("{", "{{").replace("}", "}}"));
      } else if (part instanceof Template.Dynamic dynamic) {
        sb.append('{').append(parenthesize(renderValue(dynamic.toExpression()))).append('}');
      }
    }
    return sb.append('"').toString();
  }

  private Rendered equality(Expression left, Expression right) {
    return new Rendered(
        parenthesize(render(left).code()) + " == " + parenthesize(render(right).code()),
        EndpointValueType.BOOLEAN,
        false);
  }

  private Rendered rawLocal(Identifier name) {
    Local local = local(name);
    return new Rendered(local.name(), local.type(), local.nullable());
  }

  private Local local(Identifier name) {
    Local local = locals.get(name);
    if (local == null) {
      throw new CodegenException("Endpoint rules reference the unknown variable " + name);
    }
    return local;
  }

  private static EndpointValueType.Field field(EndpointValueType type, String name) {
    if (type.kind() != EndpointValueType.Kind.RECORD) {
      throw new CodegenException("Endpoint rules read the attribute " + name + " of a non-record");
    }
    EndpointValueType.Field field = type.fields().get(name);
    if (field == null) {
      throw new CodegenException(
          "Endpoint rules read the attribute " + name + ", which " + type.record() + " lacks");
    }
    return field;
  }
}

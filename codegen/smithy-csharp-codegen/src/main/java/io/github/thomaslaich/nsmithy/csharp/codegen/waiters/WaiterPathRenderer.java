/*
 * Compiles the JMESPath of waiter acceptors into C# over the generated input and output types.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.waiters;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.jmespath.JmespathExpression;
import software.amazon.smithy.jmespath.ast.AndExpression;
import software.amazon.smithy.jmespath.ast.ComparatorExpression;
import software.amazon.smithy.jmespath.ast.CurrentExpression;
import software.amazon.smithy.jmespath.ast.FieldExpression;
import software.amazon.smithy.jmespath.ast.FilterProjectionExpression;
import software.amazon.smithy.jmespath.ast.FlattenExpression;
import software.amazon.smithy.jmespath.ast.FunctionExpression;
import software.amazon.smithy.jmespath.ast.LiteralExpression;
import software.amazon.smithy.jmespath.ast.MultiSelectListExpression;
import software.amazon.smithy.jmespath.ast.NotExpression;
import software.amazon.smithy.jmespath.ast.ObjectProjectionExpression;
import software.amazon.smithy.jmespath.ast.OrExpression;
import software.amazon.smithy.jmespath.ast.ProjectionExpression;
import software.amazon.smithy.jmespath.ast.Subexpression;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.shapes.CollectionShape;
import software.amazon.smithy.model.shapes.MapShape;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.traits.SparseTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders a waiter path as a C# expression. Every value has a JMESPath type and a C# expression
 * that may be null: strings are {@code string?}, booleans {@code bool?}, numbers {@code double?},
 * structures their generated type, lists {@code IEnumerable<T>?} of those, and maps their generated
 * dictionary type. Enums read as their string or number value. Semantics that are not plain C#
 * operators (truthiness, comparisons of non-numbers, {@code length}, {@code contains}) go through
 * {@code SmithyJmespath}.
 */
@SmithyInternalApi
final class WaiterPathRenderer {

  sealed interface Type permits Str, Bool, Num, Struct, ListOf, MapOf, Root, Null {}

  record Str() implements Type {}

  record Bool() implements Type {}

  record Num() implements Type {}

  /** A structure or union. */
  record Struct(Shape shape) implements Type {}

  record ListOf(Type item) implements Type {}

  /** A map, kept as its generated dictionary; values are converted when read. */
  record MapOf(Shape value) implements Type {}

  /** The {@code inputOutput} matcher's root: an object with {@code input} and {@code output}. */
  record Root(Value input, Value output) implements Type {}

  record Null() implements Type {}

  /** A rendered value: its C# expression and JMESPath type. */
  record Value(String code, Type type) {}

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final Model model;
  private final String helpers;
  private final String path;
  private int lambdas;

  WaiterPathRenderer(GenerationContext context, CSharpWriter writer, String path) {
    this.context = context;
    this.writer = writer;
    this.model = context.model();
    this.helpers = writer.typeName(RuntimeTypes.SMITHY_JMESPATH);
    this.path = path;
  }

  /** Renders the path over {@code root}. */
  Value render(Value root) {
    writer.addImport("System.Linq");
    return translate(JmespathExpression.parse(path), root);
  }

  private Value translate(JmespathExpression expression, Value current) {
    if (expression instanceof CurrentExpression) {
      return current;
    }
    if (expression instanceof FieldExpression field) {
      return field(current, field.getName(), expression);
    }
    if (expression instanceof Subexpression subexpression) {
      return translate(subexpression.getRight(), translate(subexpression.getLeft(), current));
    }
    if (expression instanceof LiteralExpression literal) {
      return literal(literal, expression);
    }
    if (expression instanceof NotExpression not) {
      return new Value("!" + truthy(translate(not.getExpression(), current)), new Bool());
    }
    if (expression instanceof AndExpression and) {
      return logical(and.getLeft(), and.getRight(), current, true, expression);
    }
    if (expression instanceof OrExpression or) {
      return logical(or.getLeft(), or.getRight(), current, false, expression);
    }
    if (expression instanceof ComparatorExpression comparator) {
      return compare(comparator, current);
    }
    if (expression instanceof FunctionExpression function) {
      return function(function, current);
    }
    if (expression instanceof MultiSelectListExpression multiSelect) {
      return multiSelect(multiSelect, current);
    }
    if (expression instanceof FlattenExpression flatten) {
      return flatten(translate(flatten.getExpression(), current), expression);
    }
    if (expression instanceof FilterProjectionExpression filter) {
      Value items = list(translate(filter.getLeft(), current), expression);
      Type item = ((ListOf) items.type()).item();
      String parameter = parameter();
      Value predicate = translate(filter.getComparison(), new Value(parameter, item));
      Value filtered =
          new Value(
              "(" + items.code() + ")?.Where(" + parameter + " => " + truthy(predicate) + ")",
              items.type());
      return project(filtered, filter.getRight());
    }
    if (expression instanceof ObjectProjectionExpression projection) {
      Value object = translate(projection.getLeft(), current);
      if (!(object.type() instanceof MapOf map)) {
        throw unsupported(expression, "`.*` reads the values of a map");
      }
      String parameter = parameter();
      Value converted = convert(parameter, map.value(), false);
      Value values =
          new Value(
              "("
                  + object.code()
                  + ")?.Values.Select("
                  + parameter
                  + " => "
                  + converted.code()
                  + ")",
              new ListOf(converted.type()));
      return project(values, projection.getRight());
    }
    if (expression instanceof ProjectionExpression projection) {
      return project(
          list(translate(projection.getLeft(), current), expression), projection.getRight());
    }
    throw unsupported(expression, "this expression is not supported");
  }

  /** Applies {@code right} to each item of {@code items}, dropping null results. */
  private Value project(Value items, JmespathExpression right) {
    Type item = ((ListOf) items.type()).item();
    if (right instanceof CurrentExpression) {
      return new Value(
          "(" + items.code() + ")?.Where(static item => item is not null)", items.type());
    }
    String parameter = parameter();
    Value mapped = translate(right, new Value(parameter, item));
    return new Value(
        "("
            + items.code()
            + ")?.Select("
            + parameter
            + " => "
            + mapped.code()
            + ").Where(static item => item is not null)",
        new ListOf(mapped.type()));
  }

  private Value flatten(Value value, JmespathExpression expression) {
    Value items = list(value, expression);
    if (((ListOf) items.type()).item() instanceof ListOf inner) {
      return new Value(
          "("
              + items.code()
              + ")?.Where(static items => items is not null).SelectMany(static items => items!)"
              + ".Where(static item => item is not null)",
          inner);
    }
    return new Value(
        "(" + items.code() + ")?.Where(static item => item is not null)", items.type());
  }

  private Value field(Value current, String name, JmespathExpression expression) {
    if (current.type() instanceof Root root) {
      return switch (name) {
        case "input" -> root.input();
        case "output" -> root.output();
        default -> throw unsupported(expression, "an inputOutput path starts at input or output");
      };
    }
    if (!(current.type() instanceof Struct struct)) {
      throw unsupported(expression, "fields are read from structures");
    }
    Shape container = struct.shape();
    MemberShape member =
        container
            .getMember(name)
            .orElseThrow(
                () -> unsupported(expression, container.getId() + " has no member `" + name + "`"));
    String code;
    if (container.isUnionShape()) {
      String variant =
          writer.typeName(context.symbolProvider().toSymbol(container))
              + "."
              + CSharpNaming.variantName(member);
      code = "(" + current.code() + " as " + variant + ")?.Value";
    } else {
      code = "(" + current.code() + ")?." + CSharpNaming.memberName(member);
    }
    return convert(code, model.expectShape(member.getTarget()), true);
  }

  /**
   * Converts a value of {@code shape} to its JMESPath representation. {@code nullable} says whether
   * {@code code} has a nullable C# type, which decides how an enum's value is read.
   */
  private Value convert(String code, Shape shape, boolean nullable) {
    // Only enum shapes generate enum types; a string with the @enum trait stays a string.
    if (shape.isEnumShape()) {
      return new Value("(" + code + ")" + (nullable ? "?" : "") + ".Value", new Str());
    }
    if (shape.isIntEnumShape()) {
      return new Value("(double?)(" + code + ")" + (nullable ? "?" : "") + ".Value", new Num());
    }
    return switch (shape.getType()) {
      case STRING -> new Value(code, new Str());
      case BOOLEAN -> new Value("(bool?)(" + code + ")", new Bool());
      case BYTE, SHORT, INTEGER, LONG, FLOAT, DOUBLE, BIG_INTEGER, BIG_DECIMAL ->
          new Value("(double?)(" + code + ")", new Num());
      case STRUCTURE, UNION -> new Value(code, new Struct(shape));
      case LIST, SET -> {
        CollectionShape collection = (CollectionShape) shape;
        Shape item = model.expectShape(collection.getMember().getTarget());
        String parameter = parameter();
        Value converted = convert(parameter, item, shape.hasTrait(SparseTrait.class));
        if (converted.code().equals(parameter)) {
          yield new Value(code, new ListOf(converted.type()));
        }
        yield new Value(
            "(" + code + ")?.Select(" + parameter + " => " + converted.code() + ")",
            new ListOf(converted.type()));
      }
      case MAP ->
          new Value(code, new MapOf(model.expectShape(((MapShape) shape).getValue().getTarget())));
      default ->
          throw new CodegenException(
              "Waiter path `"
                  + path
                  + "` reads a "
                  + shape.getType()
                  + ", which waiters do not support.");
    };
  }

  private Value literal(LiteralExpression literal, JmespathExpression expression) {
    if (literal.isStringValue()) {
      return new Value(CSharpNaming.formatString(literal.expectStringValue()), new Str());
    }
    if (literal.isBooleanValue()) {
      return new Value("(bool?)" + literal.expectBooleanValue(), new Bool());
    }
    if (literal.isNumberValue()) {
      String number = new BigDecimal(literal.expectNumberValue().toString()).toPlainString();
      return new Value("(double?)" + number + "d", new Num());
    }
    if (literal.isNullValue()) {
      return new Value("null", new Null());
    }
    throw unsupported(expression, "only string, boolean, number, and null literals are supported");
  }

  /**
   * {@code a && b} is {@code b} when {@code a} is truthy, else {@code a}; {@code ||} the reverse.
   */
  private Value logical(
      JmespathExpression leftExpression,
      JmespathExpression rightExpression,
      Value current,
      boolean and,
      JmespathExpression expression) {
    Value left = translate(leftExpression, current);
    Value right = translate(rightExpression, current);
    if (!left.type().equals(right.type()) || left.type() instanceof Null) {
      throw unsupported(expression, "both sides must have the same type");
    }
    if (left.type() instanceof Bool || left.type() instanceof Str || left.type() instanceof Num) {
      return new Value(
          helpers + (and ? ".And(" : ".Or(") + left.code() + ", " + right.code() + ")",
          left.type());
    }
    // Lists and structures: the left side is rendered twice, but has no side effects.
    String first = and ? right.code() : left.code();
    String second = and ? left.code() : right.code();
    return new Value("(" + truthy(left) + " ? " + first + " : " + second + ")", left.type());
  }

  private Value compare(ComparatorExpression comparator, Value current) {
    Value left = translate(comparator.getLeft(), current);
    Value right = translate(comparator.getRight(), current);
    return switch (comparator.getComparator()) {
      case EQUAL -> new Value(equality(left, right, comparator), new Bool());
      case NOT_EQUAL -> new Value("!" + equality(left, right, comparator), new Bool());
      case LESS_THAN -> ordering("LessThan", left, right);
      case LESS_THAN_EQUAL -> ordering("LessThanOrEqual", left, right);
      case GREATER_THAN -> ordering("GreaterThan", left, right);
      case GREATER_THAN_EQUAL -> ordering("GreaterThanOrEqual", left, right);
    };
  }

  private String equality(Value left, Value right, JmespathExpression expression) {
    if (left.type() instanceof Null) {
      return "(" + right.code() + " is null)";
    }
    if (right.type() instanceof Null) {
      return "(" + left.code() + " is null)";
    }
    boolean scalar =
        left.type() instanceof Str || left.type() instanceof Bool || left.type() instanceof Num;
    if (!scalar) {
      throw unsupported(expression, "only strings, booleans, and numbers are compared");
    }
    if (!left.type().equals(right.type())) {
      // Values of different types are never equal.
      return "false";
    }
    return "(" + left.code() + " == " + right.code() + ")";
  }

  /** Ordering compares numbers; any other operands yield null. */
  private Value ordering(String method, Value left, Value right) {
    if (!(left.type() instanceof Num) || !(right.type() instanceof Num)) {
      return new Value("(bool?)null", new Bool());
    }
    return new Value(
        helpers + "." + method + "(" + left.code() + ", " + right.code() + ")", new Bool());
  }

  private Value function(FunctionExpression function, Value current) {
    List<Value> arguments = new ArrayList<>();
    for (JmespathExpression argument : function.getArguments()) {
      arguments.add(translate(argument, current));
    }
    return switch (function.getName()) {
      case "length" -> length(single(arguments, function), function);
      case "contains" -> contains(arguments, function);
      default ->
          throw unsupported(function, "the only functions supported are length and contains");
    };
  }

  private Value length(Value subject, FunctionExpression function) {
    if (!(subject.type() instanceof Str
        || subject.type() instanceof ListOf
        || subject.type() instanceof MapOf)) {
      throw unsupported(function, "length takes a string, list, or map");
    }
    return new Value(helpers + ".Length(" + subject.code() + ")", new Num());
  }

  private Value contains(List<Value> arguments, FunctionExpression function) {
    if (arguments.size() != 2) {
      throw unsupported(function, "contains takes two arguments");
    }
    Value subject = arguments.get(0);
    Value search = arguments.get(1);
    boolean matches =
        subject.type() instanceof Str
            ? search.type() instanceof Str
            : subject.type() instanceof ListOf list && list.item().equals(search.type());
    if (!matches) {
      throw unsupported(function, "contains takes a string or list and a value of its type");
    }
    return new Value(
        helpers + ".Contains(" + subject.code() + ", " + search.code() + ")", new Bool());
  }

  private Value multiSelect(MultiSelectListExpression multiSelect, Value current) {
    List<Value> items = new ArrayList<>();
    for (JmespathExpression item : multiSelect.getExpressions()) {
      items.add(translate(item, current));
    }
    Type type = items.get(0).type();
    if (items.stream().anyMatch(item -> !item.type().equals(type))) {
      throw unsupported(multiSelect, "the items of a multi-select list must have the same type");
    }
    StringBuilder code =
        new StringBuilder("new ").append(csType(type, multiSelect)).append("[] { ");
    for (int i = 0; i < items.size(); i++) {
      code.append(i == 0 ? "" : ", ").append(items.get(i).code());
    }
    return new Value(code.append(" }").toString(), new ListOf(type));
  }

  /** {@code value} as a list; anything else fails, since its projection would always be null. */
  private Value list(Value value, JmespathExpression expression) {
    if (value.type() instanceof ListOf) {
      return value;
    }
    throw unsupported(expression, "projections read lists");
  }

  /** A boolean C# expression for the value's JMESPath truthiness. */
  private String truthy(Value value) {
    if (value.type() instanceof Null) {
      return "false";
    }
    return helpers + ".IsTruthy(" + value.code() + ")";
  }

  /** The nullable C# type of a value of {@code type}, for array element types. */
  private String csType(Type type, JmespathExpression expression) {
    return switch (type) {
      case Str s -> "string?";
      case Bool b -> "bool?";
      case Num n -> "double?";
      case Struct s -> writer.typeName(context.symbolProvider().toSymbol(s.shape())) + "?";
      case ListOf l ->
          writer.typeName(RuntimeTypes.I_ENUMERABLE) + "<" + csType(l.item(), expression) + ">?";
      default -> throw unsupported(expression, "this value cannot be an item of a list");
    };
  }

  private static Value single(List<Value> arguments, FunctionExpression function) {
    if (arguments.size() != 1) {
      throw new CodegenException(function.getName() + " takes one argument.");
    }
    return arguments.get(0);
  }

  private String parameter() {
    return "item" + ++lambdas;
  }

  private CodegenException unsupported(JmespathExpression expression, String reason) {
    return new CodegenException(
        "Unsupported waiter path `" + path + "` at `" + expression + "`: " + reason + ".");
  }
}

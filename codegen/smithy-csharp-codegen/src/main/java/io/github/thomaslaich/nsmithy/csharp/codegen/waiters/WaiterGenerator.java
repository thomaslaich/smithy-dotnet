/*
 * Generates a service's waiter acceptor tables.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.waiters;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.Bool;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.ListOf;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.Root;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.Str;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.Struct;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterPathRenderer.Value;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.Optional;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.OperationIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.utils.SmithyInternalApi;
import software.amazon.smithy.waiters.Acceptor;
import software.amazon.smithy.waiters.Matcher;
import software.amazon.smithy.waiters.PathMatcher;
import software.amazon.smithy.waiters.Waiter;

/**
 * Renders {@code {Service}Waiters}: one acceptor table per waiter, each acceptor a static lambda
 * over the attempt's input, output, and exception, with its matcher as a comment.
 */
@SmithyInternalApi
public final class WaiterGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final Model model;

  public WaiterGenerator(GenerationContext context, CSharpWriter writer, ServiceShape service) {
    this.context = context;
    this.writer = writer;
    this.service = service;
    this.model = context.model();
  }

  @Override
  public void run() {
    writer.pushState();
    try {
      writer.putContext("class", WaiterMethods.waitersClass(service));
      writer.putContext(
          "tables",
          writer.consumer(
              w ->
                  TopDownIndex.of(model).getContainedOperations(service).stream()
                      .sorted(Comparator.comparing(op -> op.getId().getName()))
                      .forEach(
                          op ->
                              WaiterMethods.waiters(context, op)
                                  .forEach((name, waiter) -> writeTable(op, name, waiter)))));
      writer.write(
          """
          internal static class ${class:L}
          {
              ${tables:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeTable(OperationShape op, String name, Waiter waiter) {
    writer.pushState();
    try {
      writer.putContext("acceptor", RuntimeTypes.SMITHY_WAITER_ACCEPTOR);
      writer.putContext("input", type(op.getInputShape()));
      writer.putContext("output", type(op.getOutputShape()));
      writer.putContext("name", CSharpNaming.typeName(name));
      writer.putContext(
          "acceptors",
          writer.consumer(
              w -> {
                for (Acceptor acceptor : waiter.getAcceptors()) {
                  writeAcceptor(op, name, acceptor);
                }
              }));
      writer.write(
          """
          internal static readonly ${acceptor:T}<${input:L}, ${output:L}>[] ${name:L} =
          [
              ${acceptors:C|}
          ];
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeAcceptor(OperationShape op, String waiter, Acceptor acceptor) {
    String state =
        switch (acceptor.getState()) {
          case SUCCESS -> "Success";
          case FAILURE -> "Failure";
          case RETRY -> "Retry";
        };
    writer.write("// $L", describe(acceptor.getMatcher()));
    writer.write(
        "new($T.$L, static (input, output, error) => $L),",
        RuntimeTypes.SMITHY_WAITER_STATE,
        state,
        matches(op, waiter, acceptor.getMatcher()));
  }

  private String matches(OperationShape op, String waiter, Matcher<?> matcher) {
    return switch (matcher) {
      case Matcher.OutputMember output -> {
        Value root = new Value("output", new Struct(model.expectShape(op.getOutputShape())));
        yield "error is null && " + compare(waiter, output.getValue(), root);
      }
      case Matcher.InputOutputMember inputOutput -> {
        Value input = new Value("input", new Struct(model.expectShape(op.getInputShape())));
        Value output = new Value("output", new Struct(model.expectShape(op.getOutputShape())));
        yield "error is null && "
            + compare(waiter, inputOutput.getValue(), new Value("", new Root(input, output)));
      }
      case Matcher.SuccessMember success ->
          success.getValue() ? "error is null" : "error is not null";
      case Matcher.ErrorTypeMember errorType -> errorMatches(op, errorType.getValue());
      default ->
          throw new CodegenException(
              "Waiter " + waiter + " of " + op.getId() + " has an unknown matcher.");
    };
  }

  private String compare(String waiter, PathMatcher matcher, Value root) {
    Value value;
    try {
      value = new WaiterPathRenderer(context, writer, matcher.getPath()).render(root);
    } catch (CodegenException e) {
      throw new CodegenException("Waiter " + waiter + ": " + e.getMessage(), e);
    }
    String expected = matcher.getExpected();
    String helpers = writer.typeName(RuntimeTypes.SMITHY_JMESPATH);
    return switch (matcher.getComparator()) {
      case STRING_EQUALS -> {
        require(waiter, matcher, value.type() instanceof Str);
        yield "(" + value.code() + ") == " + CSharpNaming.formatString(expected);
      }
      case BOOLEAN_EQUALS -> {
        require(waiter, matcher, value.type() instanceof Bool);
        yield "(" + value.code() + ") == " + Boolean.parseBoolean(expected);
      }
      case ALL_STRING_EQUALS -> {
        require(waiter, matcher, value.type() instanceof ListOf list && list.item() instanceof Str);
        yield helpers
            + ".AllStringEquals("
            + value.code()
            + ", "
            + CSharpNaming.formatString(expected)
            + ")";
      }
      case ANY_STRING_EQUALS -> {
        require(waiter, matcher, value.type() instanceof ListOf list && list.item() instanceof Str);
        yield helpers
            + ".AnyStringEquals("
            + value.code()
            + ", "
            + CSharpNaming.formatString(expected)
            + ")";
      }
    };
  }

  private static void require(String waiter, PathMatcher matcher, boolean matches) {
    if (!matches) {
      throw new CodegenException(
          "Waiter "
              + waiter
              + ": the path `"
              + matcher.getPath()
              + "` does not produce a value the "
              + matcher.getComparator()
              + " comparator can compare.");
    }
  }

  /**
   * A modeled error of the operation matches by its type; any other name matches an unmodeled error
   * by the error code the protocol read.
   */
  private String errorMatches(OperationShape op, String errorType) {
    String name = errorType.substring(errorType.lastIndexOf('#') + 1);
    Optional<StructureShape> modeled =
        OperationIndex.of(model).getErrors(service, op).stream()
            .filter(
                error ->
                    error.getId().toString().equals(errorType)
                        || error.getId().getName().equals(errorType))
            .findFirst();
    if (modeled.isPresent()) {
      return "error is " + writer.typeName(context.symbolProvider().toSymbol(modeled.get()));
    }
    return writer.format(
        "error is $T { ErrorCode: $L }",
        RuntimeTypes.SMITHY_CLIENT_EXCEPTION,
        CSharpNaming.formatString(name));
  }

  private static String describe(Matcher<?> matcher) {
    return switch (matcher) {
      case Matcher.OutputMember output -> "output: " + describe(output.getValue());
      case Matcher.InputOutputMember inputOutput ->
          "inputOutput: " + describe(inputOutput.getValue());
      case Matcher.SuccessMember success -> "success: " + success.getValue();
      case Matcher.ErrorTypeMember errorType -> "errorType: " + errorType.getValue();
      default -> "unknown";
    };
  }

  private static String describe(PathMatcher matcher) {
    return matcher.getPath() + " " + matcher.getComparator() + " \"" + matcher.getExpected() + "\"";
  }

  private String type(ShapeId id) {
    return writer.typeName(context.symbolProvider().toSymbol(model.expectShape(id)));
  }
}

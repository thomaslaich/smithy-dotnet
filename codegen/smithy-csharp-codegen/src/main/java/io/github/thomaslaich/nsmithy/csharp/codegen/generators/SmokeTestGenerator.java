/*
 * Generates a service's smoke tests.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.sections.SmokeTestConfigSection;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.model.knowledge.OperationIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.smoketests.traits.Expectation;
import software.amazon.smithy.smoketests.traits.SmokeTestCase;
import software.amazon.smithy.smoketests.traits.SmokeTestsTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders {@code {Service}SmokeTests}: one {@code SmithySmokeTest} per {@code @smokeTests} case of
 * the service's operations, which configures a copy of the caller's configuration, creates a client
 * from it, and calls the operation with the case's input.
 */
@SmithyInternalApi
public final class SmokeTestGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final ClientExamplesGenerator values;

  public SmokeTestGenerator(GenerationContext context, CSharpWriter writer, ServiceShape service) {
    this.context = context;
    this.writer = writer;
    this.service = service;
    this.values = new ClientExamplesGenerator(context, writer);
  }

  /** Whether any operation of {@code service} has a smoke test. */
  public static boolean hasSmokeTests(GenerationContext context, ServiceShape service) {
    return !operations(context, service).isEmpty();
  }

  /** The name of the generated class holding {@code service}'s smoke tests. */
  public static String smokeTestsClass(ServiceShape service) {
    return CSharpNaming.typeName(service.getId().getName()) + "SmokeTests";
  }

  private static List<OperationShape> operations(GenerationContext context, ServiceShape service) {
    return TopDownIndex.of(context.model()).getContainedOperations(service).stream()
        .filter(op -> op.hasTrait(SmokeTestsTrait.class))
        .sorted(Comparator.comparing(op -> op.getId().getName()))
        .toList();
  }

  @Override
  public void run() {
    String client = CSharpNaming.typeName(service.getId().getName()) + "Client";
    writer.pushState();
    try {
      writer.putContext("class", smokeTestsClass(service));
      writer.putContext("smokeTest", RuntimeTypes.SMITHY_SMOKE_TEST);
      writer.putContext("list", RuntimeTypes.I_READ_ONLY_LIST);
      writer.putContext("config", client + "Config");
      writer.putContext("client", client);
      writer.putContext("interface", "I" + client);
      writer.putContext(
          "tests",
          writer.consumer(
              w -> {
                for (OperationShape op : operations(context, service)) {
                  for (SmokeTestCase testCase :
                      op.expectTrait(SmokeTestsTrait.class).getTestCases()) {
                    writeTest(op, testCase);
                  }
                }
              }));
      writer.write(
          """
          /// <summary>The smoke tests of the service's operations, from their <c>@smokeTests</c> traits.</summary>
          public static class ${class:L}
          {
              public static ${list:T}<${smokeTest:T}<${config:L}, ${interface:L}>> All { get; } =
              [
                  ${tests:C|}
              ];
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeTest(OperationShape op, SmokeTestCase testCase) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    writer.pushState();
    try {
      writer.putContext("id", CSharpNaming.formatString(testCase.getId()));
      writer.putContext("operation", CSharpNaming.formatString(op.getId().getName()));
      writer.putContext("expectation", expectation(op, testCase.getExpectation()));
      writer.putContext(
          "tags",
          testCase.getTags().stream()
              .map(CSharpNaming::formatString)
              .collect(Collectors.joining(", ")));
      writer.putContext(
          "configure",
          writer.consumer(
              w -> {
                w.write("var configured = new $L(config);", w.getContext("config"));
                w.injectSection(new SmokeTestConfigSection(service, op, testCase));
                w.write("return configured;");
              }));
      writer.putContext("method", CSharpNaming.typeName(op.getId().getName()) + "Async");
      writer.putContext(
          "input",
          hasInput
              ? values.expression(
                      op.getInputShape(),
                      testCase.getParams().map(Node.class::cast).orElse(Node.objectNode()))
                  + ", "
              : "");
      writer.write(
          """
          new(
              ${id:L},
              ${operation:L},
              ${expectation:L},
              [${tags:L}],
              static config =>
              {
                  ${configure:C|}
              },
              static config => new ${client:L}(config),
              static (client, cancellationToken) =>
                  client.${method:L}(${input:L}cancellationToken)
          ),\
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * A failure names its error by shape id. The operation's and service's modeled errors match by
   * their generated type, and any other error by the code an unmodeled error carries.
   */
  private String expectation(OperationShape op, Expectation expectation) {
    if (expectation.isSuccess()) {
      return writer.format("$T.Success", RuntimeTypes.SMITHY_SMOKE_TEST_EXPECTATION);
    }
    List<StructureShape> modeled = OperationIndex.of(context.model()).getErrors(service, op);
    Optional<ShapeId> errorId = expectation.getFailure().flatMap(failure -> failure.getErrorId());
    String matches;
    if (errorId.isPresent()) {
      matches =
          modeled.stream()
              .filter(error -> error.getId().equals(errorId.get()))
              .findFirst()
              .map(error -> "exception is " + type(error))
              .orElseGet(
                  () ->
                      writer.format(
                          "exception is $T { ErrorCode: $L }",
                          RuntimeTypes.SMITHY_CLIENT_EXCEPTION,
                          CSharpNaming.formatString(errorId.get().getName())));
    } else {
      List<String> types = new ArrayList<>();
      types.add(writer.typeName(RuntimeTypes.SMITHY_CLIENT_EXCEPTION));
      modeled.stream()
          .sorted(Comparator.comparing(error -> error.getId().getName()))
          .forEach(error -> types.add(type(error)));
      matches = "exception is " + String.join(" or ", types);
    }
    return writer.format(
        "$T.Failure($L, static exception => $L)",
        RuntimeTypes.SMITHY_SMOKE_TEST_EXPECTATION,
        errorId.map(id -> CSharpNaming.formatString(id.toString())).orElse("null"),
        matches);
  }

  private String type(StructureShape shape) {
    return writer.typeName(context.symbolProvider().toSymbol(shape));
  }
}

/*
 * Renders a service's endpoint parameters record and its BDD-walking endpoint resolver.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointExpressionRenderer.Local;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointExpressionRenderer.Rendered;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.SchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.Node;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeType;
import software.amazon.smithy.model.traits.EnumTrait;
import software.amazon.smithy.rulesengine.language.Endpoint;
import software.amazon.smithy.rulesengine.language.evaluation.value.Value;
import software.amazon.smithy.rulesengine.language.syntax.Identifier;
import software.amazon.smithy.rulesengine.language.syntax.expressions.Expression;
import software.amazon.smithy.rulesengine.language.syntax.expressions.Template;
import software.amazon.smithy.rulesengine.language.syntax.expressions.literal.Literal;
import software.amazon.smithy.rulesengine.language.syntax.expressions.literal.LiteralVisitor;
import software.amazon.smithy.rulesengine.language.syntax.parameters.Parameter;
import software.amazon.smithy.rulesengine.language.syntax.rule.Condition;
import software.amazon.smithy.rulesengine.language.syntax.rule.EndpointRule;
import software.amazon.smithy.rulesengine.language.syntax.rule.ErrorRule;
import software.amazon.smithy.rulesengine.language.syntax.rule.Rule;
import software.amazon.smithy.rulesengine.logic.bdd.Bdd;
import software.amazon.smithy.rulesengine.traits.ContextIndex;
import software.amazon.smithy.rulesengine.traits.ContextParamTrait;
import software.amazon.smithy.rulesengine.traits.EndpointBddTrait;
import software.amazon.smithy.rulesengine.traits.OperationContextParamDefinition;
import software.amazon.smithy.rulesengine.traits.OperationContextParamsTrait;
import software.amazon.smithy.rulesengine.traits.StaticContextParamsTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * Renders {@code {Service}EndpointParameters} and {@code {Service}EndpointResolver}. The resolver
 * walks the BDD over a constant node table; conditions and results are {@code switch} cases over
 * their indexes, each commented with the rules expression it came from.
 */
@SmithyInternalApi
public final class EndpointResolverGenerator implements Runnable {

  private static final int RESULT_OFFSET = Bdd.RESULT_OFFSET;
  private static final Set<String> RESERVED_LOCALS =
      Set.of("parameters", "nodes", "reference", "index", "outcome", "condition", "result");

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final EndpointRules rules;
  private final String parametersType;
  private final String resolverType;
  private final String configType;
  private Map<Identifier, Local> parameterLocals = Map.of();
  private boolean usesDefaultAuthSchemes;

  public EndpointResolverGenerator(
      GenerationContext context, CSharpWriter writer, EndpointRules rules) {
    this.context = context;
    this.writer = writer;
    this.rules = rules;
    this.parametersType = EndpointRules.parametersTypeName(rules.service());
    this.resolverType = EndpointRules.resolverTypeName(rules.service());
    this.configType = CSharpNaming.typeName(rules.service().getId().getName()) + "ClientConfig";
  }

  @Override
  public void run() {
    writeParameters();
    writer.write("");
    writeResolver();
  }

  // ---------------- parameters ----------------

  private void writeParameters() {
    writer.writeXmlDocs(
        "Endpoint rule parameters of the " + rules.service().getId().getName() + " service.",
        Map.of());
    writer.pushState();
    try {
      writer.putContext("parametersType", parametersType);
      writer.putContext("properties", writer.consumer(w -> writeParameterProperties()));
      writer.write(
          """
          public sealed record ${parametersType:L}
          {
              ${properties:C|}
          }\
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeParameterProperties() {
    boolean first = true;
    for (Parameter parameter : rules.bdd().getParameters()) {
      if (!first) {
        writer.write("");
      }
      first = false;
      String doc = parameter.getDocumentation().orElse(null);
      if (parameter.getDeprecated().isPresent()) {
        String message = parameter.getDeprecated().get().getMessage().orElse(null);
        doc =
            (doc == null ? "" : doc + " ")
                + "Deprecated"
                + (message == null ? "." : ": " + message);
      }
      if (doc != null) {
        writer.writeXmlDocs(doc, Map.of());
      }
      String type = valueType(parameter).render(writer);
      if (parameter.getDefault().isPresent()) {
        writer.write(
            "public $L $L { get; init; } = $L;",
            type,
            propertyName(parameter),
            defaultLiteral(parameter.getDefault().get(), valueType(parameter)));
      } else {
        writer.write("public $L? $L { get; init; }", type, propertyName(parameter));
      }
    }
  }

  // ---------------- resolver ----------------

  private void writeResolver() {
    Map<String, List<String>> bindings = operationBindings();
    Bdd bdd = rules.bdd().getBdd();

    writer.writeXmlDocs(
        "Resolves endpoints of the "
            + rules.service().getId().getName()
            + " service from its modeled endpoint rules.",
        Map.of());
    writer.pushState();
    try {
      boolean bindsPerOperation = !bindings.isEmpty();
      writer.putContext("resolverType", resolverType);
      writer.putContext("parametersType", parametersType);
      writer.putContext("endpointResolver", RuntimeTypes.I_ENDPOINT_RESOLVER);
      writer.putContext("endpoint", RuntimeTypes.SMITHY_ENDPOINT);
      writer.putContext("endpointParameters", RuntimeTypes.SMITHY_ENDPOINT_PARAMETERS);
      writer.putContext("valueTask", RuntimeTypes.VALUE_TASK);
      writer.putContext("cancellationToken", RuntimeTypes.CANCELLATION_TOKEN);
      writer.putContext("argumentNull", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("span", RuntimeTypes.READ_ONLY_SPAN);
      writer.putContext("root", bdd.getRootRef());
      writer.putContext("resultOffset", RESULT_OFFSET);
      writer.putContext("bindsPerOperation", bindsPerOperation);
      // The generated client, and with it its configuration class, exists only for services with
      // a supported protocol.
      writer.putContext(
          "hasConfig",
          context.settings().generateClient()
              && !ProtocolSupport.protocols(context, rules.service()).isEmpty());
      writer.putContext(
          "nodes",
          writer.consumer(
              w -> {
                for (int i = 0; i < bdd.getNodeCount(); i++) {
                  writer.write("$L, $L, $L,", bdd.getVariable(i), bdd.getHigh(i), bdd.getLow(i));
                }
              }));
      writer.putContext(
          "constructorDocs",
          writer.consumer(
              w ->
                  writer.writeXmlDocs(
                      "Creates a resolver for the given client-level parameters."
                          + (bindsPerOperation
                              ? ""
                              : " They fix the endpoint for every operation, so it is resolved"
                                  + " here."),
                      Map.of())));
      writer.putContext("configConstructor", writer.consumer(w -> writeConfigConstructor()));
      writer.putContext("bind", writer.consumer(w -> writeBind(bindings)));
      writer.putContext("resolve", writer.consumer(w -> writeResolve()));
      writer.write(
          """
          public sealed class ${resolverType:L} : ${endpointResolver:T}
          {
              private const int Root = ${root:L};
              private const int ResultOffset = ${resultOffset:L};

              // (condition, ifTrue, ifFalse) per node.
              // csharpier-ignore
              private static ${span:T}<int> Nodes => new int[]
              {
                  ${nodes:C|}
              };

              private readonly ${parametersType:L} clientParameters;
              ${?bindsPerOperation}
              private ${endpoint:T}? clientEndpoint;
              ${/bindsPerOperation}

              ${constructorDocs:C|}
              public ${resolverType:L}(${parametersType:L} clientParameters)
              {
                  ${argumentNull:T}.ThrowIfNull(clientParameters);
                  this.clientParameters = clientParameters;
                  ${^bindsPerOperation}
                  StaticEndpoint = Resolve(clientParameters);
                  ${/bindsPerOperation}
              }

              ${?hasConfig}
              ${configConstructor:C|}

              ${/hasConfig}
              public ${endpoint:T}? StaticEndpoint { get; }

              public ${valueTask:T}<${endpoint:T}> ResolveEndpointAsync(${endpointParameters:T} parameters, ${cancellationToken:T} cancellationToken = default)
              {
                  ${argumentNull:T}.ThrowIfNull(parameters);
                  cancellationToken.ThrowIfCancellationRequested();
                  ${?bindsPerOperation}
                  var bound = Bind(parameters);
                  return ${valueTask:T}.FromResult(bound is null ? clientEndpoint ??= Resolve(clientParameters) : Resolve(bound));
                  ${/bindsPerOperation}
                  ${^bindsPerOperation}
                  return ${valueTask:T}.FromResult(StaticEndpoint!);
                  ${/bindsPerOperation}
              }

              ${?bindsPerOperation}
              ${bind:C|}

              ${/bindsPerOperation}
              ${resolve:C|}

              private static bool IsNode(int reference) => reference is > 1 and < ResultOffset or < -1 and > -ResultOffset;
          }\
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeConfigConstructor() {
    List<String> assignments = new ArrayList<>();
    rules
        .endpointParameter()
        .ifPresent(
            parameter ->
                assignments.add(
                    propertyName(parameter)
                        + " = (endpoint ?? config.Endpoint)?.OriginalString"
                        + defaultFallback(parameter)));
    for (EndpointRules.ConfigParameter config : rules.configParameters()) {
      assignments.add(
          propertyName(config.parameter())
              + " = config."
              + config.property()
              + defaultFallback(config.parameter()));
    }

    writer.writeXmlDocs(
        "Creates a resolver for a client's configuration. The endpoint, when set, is the rules'"
            + " custom endpoint.",
        Map.of());
    writer.pushState();
    try {
      writer.putContext("configType", configType);
      writer.putContext("uri", RuntimeTypes.URI);
      writer.putContext("hasAssignments", !assignments.isEmpty());
      writer.putContext(
          "assignments",
          writer.consumer(w -> assignments.forEach(assignment -> writer.write("$L,", assignment))));
      writer.write(
          """
          public ${resolverType:L}(${configType:L} config, ${uri:T}? endpoint = null)
              : this(ClientParameters(config, endpoint))
          {
          }

          private static ${parametersType:L} ClientParameters(${configType:L} config, ${uri:T}? endpoint)
          {
              ${argumentNull:T}.ThrowIfNull(config);
              ${?hasAssignments}
              return new ${parametersType:L}
              {
                  ${assignments:C|}
              };
              ${/hasAssignments}
              ${^hasAssignments}
              return new ${parametersType:L}();
              ${/hasAssignments}
          }\
          """);
    } finally {
      writer.popState();
    }
  }

  private String defaultFallback(Parameter parameter) {
    return parameter
        .getDefault()
        .map(value -> " ?? " + defaultLiteral(value, valueType(parameter)))
        .orElse("");
  }

  private void writeBind(Map<String, List<String>> bindings) {
    writer.pushState();
    try {
      writer.putContext(
          "arms",
          writer.consumer(
              w -> {
                for (Map.Entry<String, List<String>> binding : bindings.entrySet()) {
                  writer.pushState();
                  try {
                    writer.putContext("pattern", binding.getKey());
                    writer.putContext(
                        "assignments",
                        writer.consumer(
                            w2 ->
                                binding
                                    .getValue()
                                    .forEach(assignment -> writer.write("$L,", assignment))));
                    writer.write(
                        """
                        ${pattern:L}
                            => clientParameters with
                            {
                                ${assignments:C|}
                            },\
                        """);
                  } finally {
                    writer.popState();
                  }
                }
              }));
      writer.write(
          """
          private ${parametersType:L}? Bind(${endpointParameters:T} parameters) => parameters.OperationId.Name switch
          {
              ${arms:C|}
              _ => null,
          };\
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeResolve() {
    EndpointBddTrait bdd = rules.bdd();
    Map<Identifier, Local> locals = new LinkedHashMap<>();
    Set<String> used = new HashSet<>(RESERVED_LOCALS);
    List<String> declarations = new ArrayList<>();
    for (Parameter parameter : bdd.getParameters()) {
      String name = allocate(parameter.getNameString(), used);
      locals.put(
          parameter.getName(),
          new Local(name, valueType(parameter), parameter.getDefault().isEmpty()));
      String fallback = valueType(parameter).isValueType() ? "" : defaultFallback(parameter);
      declarations.add("var " + name + " = parameters." + propertyName(parameter) + fallback + ";");
      if (parameter.isRequired() && parameter.getDefault().isEmpty()) {
        declarations.add(
            writer.format(
                "if ($L is null) throw new $T($L);",
                name,
                RuntimeTypes.SMITHY_ENDPOINT_RESOLUTION_EXCEPTION,
                CSharpNaming.formatString(
                    "Endpoint parameter '" + parameter.getNameString() + "' is required.")));
      }
    }
    parameterLocals = Map.copyOf(locals);

    EndpointExpressionRenderer renderer = new EndpointExpressionRenderer(writer, rules, locals);
    List<String> conditions = new ArrayList<>();
    for (Condition condition : bdd.getConditions()) {
      Rendered value = renderer.render(condition.getFunction());
      String test;
      if (condition.getResult().isPresent()) {
        Identifier result = condition.getResult().get();
        String name = allocate(result.getName().getValue(), used);
        locals.put(result, new Local(name, value.type(), true));
        declarations.add(value.type().render(writer) + "? " + name + " = null;");
        String assignment = "(" + name + " = " + value.code() + ")";
        test =
            value.type().kind() == EndpointValueType.Kind.BOOLEAN
                ? assignment + " == true"
                : assignment + " is not null";
      } else if (value.type().kind() == EndpointValueType.Kind.BOOLEAN) {
        test =
            value.nullable()
                ? EndpointExpressionRenderer.parenthesize(value.code()) + " == true"
                : value.code();
      } else {
        test =
            value.nullable()
                ? EndpointExpressionRenderer.parenthesize(value.code()) + " is not null"
                : "true";
      }
      conditions.add(test);
    }

    List<String> results = new ArrayList<>();
    List<Rule> ruleResults = bdd.getResults();
    for (int i = 0; i < ruleResults.size(); i++) {
      Rule rule = ruleResults.get(i);
      if (rule instanceof EndpointRule endpointRule) {
        results.add(endpoint(renderer, endpointRule.getEndpoint()));
      } else if (rule instanceof ErrorRule errorRule) {
        results.add(
            writer.format(
                "throw new $T($L)",
                RuntimeTypes.SMITHY_ENDPOINT_RESOLUTION_EXCEPTION,
                renderer.renderValue(errorRule.getError())));
      } else {
        results.add(null);
      }
    }

    writer.writeXmlDocs("Resolves the endpoint for a full set of endpoint parameters.", Map.of());
    writer.pushState();
    try {
      writer.putContext("resolutionException", RuntimeTypes.SMITHY_ENDPOINT_RESOLUTION_EXCEPTION);
      writer.putContext("unreachable", RuntimeTypes.UNREACHABLE_EXCEPTION);
      writer.putContext("usesDefaultAuthSchemes", usesDefaultAuthSchemes);
      writer.putContext(
          "declarations",
          writer.consumer(
              w -> declarations.forEach(declaration -> writer.write("$L", declaration))));
      writer.putContext(
          "conditions",
          writer.consumer(
              w -> {
                for (int i = 0; i < conditions.size(); i++) {
                  writer.write("// $L", comment(bdd.getConditions().get(i).toString()));
                  writer.write("$L => $L,", i, conditions.get(i));
                }
              }));
      writer.putContext(
          "results",
          writer.consumer(
              w -> {
                for (int i = 0; i < results.size(); i++) {
                  if (results.get(i) != null) {
                    writer.write("$L => $L,", i, results.get(i));
                  }
                }
              }));
      writer.putContext("defaultAuthSchemes", writer.consumer(w -> writeDefaultAuthSchemes()));
      writer.write(
          """
          public static ${endpoint:T} Resolve(${parametersType:L} parameters)
          {
              ${argumentNull:T}.ThrowIfNull(parameters);
              ${declarations:C|}

              var nodes = Nodes;
              var reference = Root;
              while (IsNode(reference))
              {
                  var index = (reference > 0 ? reference : -reference) - 1;
                  var outcome = Test(nodes[index * 3]) ^ (reference < 0);
                  reference = outcome ? nodes[index * 3 + 1] : nodes[index * 3 + 2];
              }
              return reference >= ResultOffset
                  ? Result(reference - ResultOffset)
                  : throw ${resolutionException:T}.NoMatch();

              bool Test(int condition) => condition switch
              {
                  ${conditions:C|}
                  _ => throw new ${unreachable:T}(),
              };

              ${endpoint:T} Result(int result) => result switch
              {
                  ${results:C|}
                  _ => throw ${resolutionException:T}.NoMatch(),
              };
              ${?usesDefaultAuthSchemes}

              ${defaultAuthSchemes:C|}
              ${/usesDefaultAuthSchemes}
          }\
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * The modeled auth schemes, with integration-supplied properties, for endpoints whose rules set
   * no {@code authSchemes}.
   */
  private void writeDefaultAuthSchemes() {
    writer.pushState();
    try {
      writer.putContext("readOnlyList", RuntimeTypes.I_READ_ONLY_LIST);
      writer.putContext("authScheme", RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME);
      writer.putContext("schemes", writer.consumer(w -> writeDefaultAuthSchemeEntries()));
      writer.write(
          """
          ${readOnlyList:T}<${authScheme:T}> DefaultAuthSchemes() => new ${authScheme:T}[]
          {
              ${schemes:C|}
          };\
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeDefaultAuthSchemeEntries() {
    String documentType = writer.typeName(RuntimeTypes.DOCUMENT);
    for (EndpointRules.DefaultAuthScheme scheme : rules.defaultAuthSchemes()) {
      List<String> entries = new ArrayList<>();
      scheme
          .properties()
          .forEach(
              (name, value) ->
                  entries.add(
                      "["
                          + CSharpNaming.formatString(name)
                          + "] = "
                          + documentType
                          + ".From("
                          + CSharpNaming.formatString(value)
                          + ")"));
      scheme
          .parameterProperties()
          .forEach(
              (name, parameter) -> {
                Local local = parameterLocals.get(parameter.getName());
                if (local.type().kind() != EndpointValueType.Kind.STRING
                    && local.type().kind() != EndpointValueType.Kind.BOOLEAN) {
                  throw new CodegenException(
                      "Auth scheme property "
                          + name
                          + " of "
                          + scheme.schemeId()
                          + " needs a string or boolean parameter, not "
                          + parameter.getNameString());
                }
                String value =
                    local.nullable()
                        ? local.name()
                            + " is null ? "
                            + documentType
                            + ".Null : "
                            + documentType
                            + ".From("
                            + local.name()
                            + (local.type().kind() == EndpointValueType.Kind.BOOLEAN
                                ? ".Value"
                                : "")
                            + ")"
                        : documentType + ".From(" + local.name() + ")";
                entries.add("[" + CSharpNaming.formatString(name) + "] = " + value);
              });
      if (entries.isEmpty()) {
        writer.write(
            "new $T($L),",
            RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME,
            CSharpNaming.formatString(scheme.schemeId()));
      } else {
        writer.write(
            "new $T($L, new $T<string, $T> { $L }),",
            RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME,
            CSharpNaming.formatString(scheme.schemeId()),
            RuntimeTypes.DICTIONARY,
            RuntimeTypes.DOCUMENT,
            String.join(", ", entries));
      }
    }
  }

  // ---------------- endpoint results ----------------

  private String endpoint(EndpointExpressionRenderer renderer, Endpoint endpoint) {
    StringBuilder sb = new StringBuilder();
    sb.append(
        writer.format(
            "new $T(new $T($L)",
            RuntimeTypes.SMITHY_ENDPOINT,
            RuntimeTypes.URI,
            renderer.renderValue(endpoint.getUrl())));
    if (!endpoint.getHeaders().isEmpty()) {
      sb.append(", Headers: ").append(headers(renderer, endpoint.getHeaders()));
    }
    Map<Identifier, Literal> properties = new LinkedHashMap<>(endpoint.getProperties());
    Literal authSchemes = properties.remove(Identifier.of("authSchemes"));
    if (authSchemes != null) {
      sb.append(", AuthSchemes: ").append(authSchemes(renderer, authSchemes));
    } else if (!rules.defaultAuthSchemes().isEmpty()) {
      usesDefaultAuthSchemes = true;
      sb.append(", AuthSchemes: DefaultAuthSchemes()");
    }
    if (!properties.isEmpty()) {
      sb.append(", Properties: ").append(documentMap(renderer, properties));
    }
    return sb.append(')').toString();
  }

  private String headers(
      EndpointExpressionRenderer renderer, Map<String, List<Expression>> headers) {
    String entries =
        headers.entrySet().stream()
            .map(
                header ->
                    "["
                        + CSharpNaming.formatString(header.getKey())
                        + "] = new string[] { "
                        + header.getValue().stream()
                            .map(renderer::renderValue)
                            .collect(Collectors.joining(", "))
                        + " }")
            .collect(Collectors.joining(", "));
    return writer.format(
        "new $T<string, $T<string>>($T.OrdinalIgnoreCase) { $L }",
        RuntimeTypes.DICTIONARY,
        RuntimeTypes.I_READ_ONLY_LIST,
        RuntimeTypes.STRING_COMPARER,
        entries);
  }

  private String authSchemes(EndpointExpressionRenderer renderer, Literal literal) {
    List<String> schemes = new ArrayList<>();
    literal.accept(
        new LiteralVisitor<Void>() {
          @Override
          public Void visitTuple(List<Literal> members) {
            for (Literal member : members) {
              schemes.add(authScheme(renderer, member));
            }
            return null;
          }

          @Override
          public Void visitBoolean(boolean value) {
            throw invalid();
          }

          @Override
          public Void visitString(Template value) {
            throw invalid();
          }

          @Override
          public Void visitRecord(Map<Identifier, Literal> members) {
            throw invalid();
          }

          @Override
          public Void visitInteger(int value) {
            throw invalid();
          }

          private CodegenException invalid() {
            return new CodegenException("Endpoint authSchemes must be a list: " + literal);
          }
        });
    return "new "
        + writer.typeName(RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME)
        + "[] { "
        + String.join(", ", schemes)
        + " }";
  }

  private String authScheme(EndpointExpressionRenderer renderer, Literal literal) {
    Map<Identifier, Literal> members =
        literal.accept(
            new LiteralVisitor<Map<Identifier, Literal>>() {
              @Override
              public Map<Identifier, Literal> visitRecord(Map<Identifier, Literal> members) {
                return members;
              }

              @Override
              public Map<Identifier, Literal> visitBoolean(boolean value) {
                throw invalid();
              }

              @Override
              public Map<Identifier, Literal> visitString(Template value) {
                throw invalid();
              }

              @Override
              public Map<Identifier, Literal> visitTuple(List<Literal> members) {
                throw invalid();
              }

              @Override
              public Map<Identifier, Literal> visitInteger(int value) {
                throw invalid();
              }

              private CodegenException invalid() {
                return new CodegenException("Endpoint auth schemes must be records: " + literal);
              }
            });
    Map<Identifier, Literal> properties = new LinkedHashMap<>(members);
    Literal name = properties.remove(Identifier.of("name"));
    if (name == null) {
      throw new CodegenException("Endpoint auth schemes need a name: " + literal);
    }
    String id =
        name.accept(
            new LiteralVisitor<String>() {
              @Override
              public String visitString(Template value) {
                if (value.isStatic()) {
                  return CSharpNaming.formatString(rules.authSchemeId(value.expectLiteral()));
                }
                // A name computed by the rules maps to its auth trait id when the result is built.
                String computed = renderer.template(value);
                if (rules.authSchemeIds().isEmpty()) {
                  return computed;
                }
                String arms =
                    rules.authSchemeIds().entrySet().stream()
                        .sorted(Map.Entry.comparingByKey())
                        .map(
                            entry ->
                                CSharpNaming.formatString(entry.getKey())
                                    + " => "
                                    + CSharpNaming.formatString(entry.getValue())
                                    + ", ")
                        .collect(Collectors.joining());
                return EndpointExpressionRenderer.parenthesize(computed)
                    + " switch { "
                    + arms
                    + "var other => other }";
              }

              @Override
              public String visitBoolean(boolean value) {
                throw invalid();
              }

              @Override
              public String visitRecord(Map<Identifier, Literal> members) {
                throw invalid();
              }

              @Override
              public String visitTuple(List<Literal> members) {
                throw invalid();
              }

              @Override
              public String visitInteger(int value) {
                throw invalid();
              }

              private CodegenException invalid() {
                return new CodegenException(
                    "Endpoint auth scheme names must be strings: " + literal);
              }
            });
    if (properties.isEmpty()) {
      return writer.format("new $T($L)", RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME, id);
    }
    return writer.format(
        "new $T($L, $L)",
        RuntimeTypes.SMITHY_ENDPOINT_AUTH_SCHEME,
        id,
        documentMap(renderer, properties));
  }

  private String documentMap(
      EndpointExpressionRenderer renderer, Map<Identifier, Literal> members) {
    String entries =
        members.entrySet().stream()
            .map(
                member ->
                    "["
                        + CSharpNaming.formatString(member.getKey().getName().getValue())
                        + "] = "
                        + document(renderer, member.getValue()))
            .collect(Collectors.joining(", "));
    return writer.format(
        "new $T<string, $T> { $L }", RuntimeTypes.DICTIONARY, RuntimeTypes.DOCUMENT, entries);
  }

  private String document(EndpointExpressionRenderer renderer, Literal literal) {
    String documentType = writer.typeName(RuntimeTypes.DOCUMENT);
    return literal.accept(
        new LiteralVisitor<String>() {
          @Override
          public String visitBoolean(boolean value) {
            return documentType + ".From(" + value + ")";
          }

          @Override
          public String visitString(Template value) {
            return documentType + ".From(" + renderer.template(value) + ")";
          }

          @Override
          public String visitInteger(int value) {
            return documentType + ".From(" + value + "m)";
          }

          @Override
          public String visitRecord(Map<Identifier, Literal> members) {
            return documentType + ".From(" + documentMap(renderer, members) + ")";
          }

          @Override
          public String visitTuple(List<Literal> members) {
            return documentType
                + ".From(new "
                + documentType
                + "[] { "
                + members.stream().map(m -> document(renderer, m)).collect(Collectors.joining(", "))
                + " })";
          }
        });
  }

  // ---------------- operation bindings ----------------

  private record ParameterBinding(String expression, boolean readsInput) {}

  /**
   * Builds bindings from least to most specific: operation context, member context, then static
   * context. Nullable sources fall back to the next source, ending with client values/defaults.
   * Keeping the expression and its input dependency together means a static override does not
   * accidentally require an input object.
   */
  private Map<String, List<String>> operationBindings() {
    Model model = context.model();
    ContextIndex index = ContextIndex.of(model);
    Map<String, Parameter> parameters = new LinkedHashMap<>();
    rules.bdd().getParameters().forEach(p -> parameters.put(p.getNameString(), p));

    Map<String, List<String>> bindings = new LinkedHashMap<>();
    OperationContextPathRenderer paths = new OperationContextPathRenderer(context, writer);
    List<OperationShape> operations =
        TopDownIndex.of(model).getContainedOperations(rules.service()).stream()
            .sorted(Comparator.comparing(o -> o.getId().getName()))
            .toList();
    for (OperationShape operation : operations) {
      Map<String, ParameterBinding> assignments = new LinkedHashMap<>();
      Shape input = model.expectShape(operation.getInputShape());
      for (Map.Entry<String, OperationContextParamDefinition> entry :
          index
              .getOperationContextParams(operation)
              .map(OperationContextParamsTrait::getParameters)
              .orElse(Map.of())
              .entrySet()) {
        Parameter parameter = parameters.get(entry.getKey());
        if (parameter == null) {
          continue;
        }
        OperationContextPathRenderer.Path path =
            paths.render(entry.getValue().getPath(), input, valueType(parameter).kind());
        assignments.put(
            propertyName(parameter),
            new ParameterBinding(
                path.nullable()
                    ? EndpointExpressionRenderer.parenthesize(path.code())
                        + " ?? clientParameters."
                        + propertyName(parameter)
                    : path.code(),
                true));
      }

      // Input members in declaration order, so the generated bindings are stable.
      for (MemberShape member : input.members()) {
        Parameter parameter =
            member
                .getTrait(ContextParamTrait.class)
                .map(trait -> parameters.get(trait.getName()))
                .orElse(null);
        if (parameter == null) {
          continue;
        }
        String property = propertyName(parameter);
        ParameterBinding lower = assignments.get(property);
        String fallback = lower == null ? "clientParameters." + property : lower.expression();
        assignments.put(
            property, new ParameterBinding(contextValue(member, parameter, fallback), true));
      }

      index
          .getStaticContextParams(operation)
          .map(StaticContextParamsTrait::getParameters)
          .orElse(Map.of())
          .forEach(
              (name, definition) -> {
                Parameter parameter = parameters.get(name);
                if (parameter != null) {
                  assignments.put(
                      propertyName(parameter),
                      new ParameterBinding(staticValue(definition.getValue(), parameter), false));
                }
              });

      if (assignments.isEmpty()) {
        continue;
      }
      String pattern = CSharpNaming.formatString(operation.getId().getName());
      if (assignments.values().stream().anyMatch(ParameterBinding::readsInput)) {
        pattern +=
            " when parameters.Input is "
                + SchemaGenerator.operationShapeType(writer, context, operation.getInputShape())
                + " input";
      }
      bindings.put(
          pattern,
          assignments.entrySet().stream()
              .map(assignment -> assignment.getKey() + " = " + assignment.getValue().expression())
              .toList());
    }
    return bindings;
  }

  private String staticValue(Node value, Parameter parameter) {
    return switch (valueType(parameter).kind()) {
      case STRING -> CSharpNaming.formatString(value.expectStringNode().getValue());
      case BOOLEAN -> Boolean.toString(value.expectBooleanNode().getValue());
      case STRING_LIST ->
          "new string[] { "
              + value.expectArrayNode().getElements().stream()
                  .map(e -> CSharpNaming.formatString(e.expectStringNode().getValue()))
                  .collect(Collectors.joining(", "))
              + " }";
      default -> throw new IllegalStateException();
    };
  }

  private String contextValue(MemberShape member, Parameter parameter, String fallback) {
    Shape target = context.model().expectShape(member.getTarget());
    boolean nullable = ShapeSupport.isNullable(member);
    String access = "input." + CSharpNaming.memberName(member);
    boolean isEnum = target.getType() == ShapeType.ENUM || target.hasTrait(EnumTrait.class);
    EndpointValueType.Kind kind = valueType(parameter).kind();
    boolean compatible =
        switch (kind) {
          case STRING -> target.getType() == ShapeType.STRING || isEnum;
          case BOOLEAN -> target.getType() == ShapeType.BOOLEAN;
          case STRING_LIST -> target.isListShape();
          default -> false;
        };
    if (!compatible) {
      throw new CodegenException(
          "Member "
              + member.getId()
              + " cannot bind the endpoint parameter "
              + parameter.getNameString()
              + " of type "
              + parameter.getType());
    }
    if (isEnum) {
      access += nullable ? "?.Value" : ".Value";
    }
    return nullable ? access + " ?? " + EndpointExpressionRenderer.parenthesize(fallback) : access;
  }

  // ---------------- helpers ----------------

  static EndpointValueType valueType(Parameter parameter) {
    return switch (parameter.getType()) {
      case STRING -> EndpointValueType.STRING;
      case BOOLEAN -> EndpointValueType.BOOLEAN;
      case STRING_ARRAY -> EndpointValueType.STRING_LIST;
    };
  }

  static String propertyName(Parameter parameter) {
    return CSharpNaming.propertyName(parameter.getNameString());
  }

  private static String defaultLiteral(Value value, EndpointValueType type) {
    return switch (type.kind()) {
      case STRING -> CSharpNaming.formatString(value.expectStringValue().getValue());
      case BOOLEAN -> Boolean.toString(value.expectBooleanValue().getValue());
      case STRING_LIST ->
          "new string[] { "
              + value.expectArrayValue().getValues().stream()
                  .map(v -> CSharpNaming.formatString(v.expectStringValue().getValue()))
                  .collect(Collectors.joining(", "))
              + " }";
      default -> throw new IllegalStateException();
    };
  }

  private static String allocate(String name, Set<String> used) {
    String base = CSharpNaming.parameterName(name);
    String candidate = base;
    int suffix = 2;
    while (!used.add(candidate)) {
      candidate = base + suffix++;
    }
    return candidate;
  }

  private static String comment(String source) {
    return source.replaceAll("\\s+", " ").strip();
  }
}

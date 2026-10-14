/*
 * A service's endpoint rules as the generator consumes them: one BDD, plus how each rule
 * parameter is bound.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.endpoints;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.rulesengine.language.syntax.parameters.BuiltIns;
import software.amazon.smithy.rulesengine.language.syntax.parameters.Parameter;
import software.amazon.smithy.rulesengine.language.syntax.parameters.ParameterType;
import software.amazon.smithy.rulesengine.traits.ClientContextParamDefinition;
import software.amazon.smithy.rulesengine.traits.ClientContextParamsTrait;
import software.amazon.smithy.rulesengine.traits.EndpointBddTrait;
import software.amazon.smithy.rulesengine.traits.EndpointRuleSetTrait;
import software.amazon.smithy.rulesengine.transforms.CompileBdd;
import software.amazon.smithy.utils.SmithyInternalApi;

/**
 * The endpoint rules of one service. The generator works only from the BDD form: a service that
 * ships {@code @endpointBdd} uses it as is, and one that ships only {@code @endpointRuleSet} has
 * its rule set compiled to a BDD here.
 */
@SmithyInternalApi
public final class EndpointRules {

  /** Members of SmithyClientConfig that a generated configuration property must not hide. */
  private static final Set<String> BASE_CONFIG_MEMBERS =
      Set.of(
          "Endpoint",
          "Protocol",
          "EndpointResolver",
          "DisableHostPrefixInjection",
          "UserAgent",
          "RetryStrategy",
          "OperationTimeout",
          "IdempotencyTokenProvider",
          "Interceptors",
          "AuthSchemes");

  private static final List<EndpointFunction> STANDARD_FUNCTIONS =
      List.of(
          new EndpointFunction(
              "parseURL",
              RuntimeTypes.ENDPOINT_FUNCTIONS,
              "ParseUrl",
              EndpointValueType.record(
                  RuntimeTypes.ENDPOINT_URL,
                  Map.of(
                      "scheme", EndpointValueType.field("Scheme", EndpointValueType.STRING),
                      "authority", EndpointValueType.field("Authority", EndpointValueType.STRING),
                      "path", EndpointValueType.field("Path", EndpointValueType.STRING),
                      "normalizedPath",
                          EndpointValueType.field("NormalizedPath", EndpointValueType.STRING),
                      "isIp", EndpointValueType.field("IsIp", EndpointValueType.BOOLEAN))),
              true),
          new EndpointFunction(
              "substring",
              RuntimeTypes.ENDPOINT_FUNCTIONS,
              "Substring",
              EndpointValueType.STRING,
              true),
          new EndpointFunction(
              "uriEncode",
              RuntimeTypes.ENDPOINT_FUNCTIONS,
              "UriEncode",
              EndpointValueType.STRING,
              false),
          new EndpointFunction(
              "isValidHostLabel",
              RuntimeTypes.ENDPOINT_FUNCTIONS,
              "IsValidHostLabel",
              EndpointValueType.BOOLEAN,
              false),
          new EndpointFunction(
              "split",
              RuntimeTypes.ENDPOINT_FUNCTIONS,
              "Split",
              EndpointValueType.STRING_LIST,
              false));

  /**
   * A rule parameter supplied by the generated client configuration.
   *
   * @param parameter the rule parameter.
   * @param property the configuration property name.
   * @param documentation the property's documentation, if any.
   */
  public record ConfigParameter(Parameter parameter, String property, String documentation) {}

  private final ServiceShape service;
  private final EndpointBddTrait bdd;
  private final Map<String, EndpointFunction> functions;
  private final Map<String, String> authSchemeIds;
  private final List<ConfigParameter> configParameters;
  private final Optional<Parameter> endpointParameter;

  private EndpointRules(
      ServiceShape service,
      EndpointBddTrait bdd,
      Map<String, EndpointFunction> functions,
      Map<String, String> authSchemeIds,
      List<ConfigParameter> configParameters,
      Optional<Parameter> endpointParameter) {
    this.service = service;
    this.bdd = bdd;
    this.functions = functions;
    this.authSchemeIds = authSchemeIds;
    this.configParameters = configParameters;
    this.endpointParameter = endpointParameter;
  }

  /** The service's endpoint rules, or empty when it models none. */
  public static Optional<EndpointRules> of(GenerationContext context, ServiceShape service) {
    Optional<EndpointBddTrait> bdd =
        service
            .getTrait(EndpointBddTrait.class)
            .or(
                () ->
                    service
                        .getTrait(EndpointRuleSetTrait.class)
                        .map(trait -> CompileBdd.compileBdd(trait.getEndpointRuleSet())));
    if (bdd.isEmpty()) {
      return Optional.empty();
    }

    Map<String, EndpointFunction> functions = new HashMap<>();
    Map<String, EndpointBuiltIn> builtIns = new HashMap<>();
    Map<String, String> authSchemeIds = new HashMap<>();
    STANDARD_FUNCTIONS.forEach(function -> functions.put(function.id(), function));
    for (CSharpIntegration integration : context.integrations()) {
      integration.endpointFunctions().forEach(function -> functions.put(function.id(), function));
      integration.endpointBuiltIns().forEach(builtIn -> builtIns.put(builtIn.id(), builtIn));
      authSchemeIds.putAll(integration.endpointAuthSchemeIds());
    }

    Map<String, ClientContextParamDefinition> clientContext =
        service
            .getTrait(ClientContextParamsTrait.class)
            .map(ClientContextParamsTrait::getParameters)
            .orElse(Map.of());

    List<ConfigParameter> configParameters = new ArrayList<>();
    Map<String, String> propertyOwners = new LinkedHashMap<>();
    Optional<Parameter> endpointParameter = Optional.empty();
    for (Parameter parameter : bdd.get().getParameters()) {
      String name = parameter.getNameString();
      Optional<String> builtIn = parameter.getBuiltIn();
      if (builtIn.isPresent() && builtIn.get().equals(BuiltIns.SDK_ENDPOINT.getBuiltIn().get())) {
        endpointParameter = Optional.of(parameter);
        continue;
      }

      // A client context parameter named Endpoint is the custom endpoint, like SDK::Endpoint:
      // client configuration already holds it.
      if (builtIn.isEmpty()
          && clientContext.containsKey(name)
          && CSharpNaming.propertyName(name).equals("Endpoint")
          && parameter.getType() == ParameterType.STRING) {
        endpointParameter = Optional.of(parameter);
        continue;
      }

      String property;
      String documentation;
      if (builtIn.isPresent()) {
        EndpointBuiltIn definition = builtIns.get(builtIn.get());
        if (definition == null) {
          throw new CodegenException(
              "Endpoint rules of "
                  + service.getId()
                  + " use the built-in "
                  + builtIn.get()
                  + ", which no C# integration supports.");
        }
        property = definition.property();
        documentation = definition.documentation();
      } else if (clientContext.containsKey(name)) {
        property = CSharpNaming.propertyName(name);
        documentation = clientContext.get(name).getDocumentation().orElse(null);
      } else {
        continue;
      }

      if (BASE_CONFIG_MEMBERS.contains(property)) {
        throw new CodegenException(
            "Endpoint parameter "
                + name
                + " of "
                + service.getId()
                + " would generate the client configuration property "
                + property
                + ", which SmithyClientConfig already declares.");
      }
      String owner = propertyOwners.putIfAbsent(property, name);
      if (owner != null) {
        throw new CodegenException(
            "Endpoint parameters "
                + owner
                + " and "
                + name
                + " of "
                + service.getId()
                + " both map to the client configuration property "
                + property
                + ".");
      }
      configParameters.add(
          new ConfigParameter(
              parameter,
              property,
              documentation != null ? documentation : parameter.getDocumentation().orElse(null)));
    }

    return Optional.of(
        new EndpointRules(
            service,
            bdd.get(),
            Map.copyOf(functions),
            Map.copyOf(authSchemeIds),
            List.copyOf(configParameters),
            endpointParameter));
  }

  public ServiceShape service() {
    return service;
  }

  public EndpointBddTrait bdd() {
    return bdd;
  }

  /** The function implementing the rules function {@code id}. */
  public EndpointFunction function(String id) {
    EndpointFunction function = functions.get(id);
    if (function == null) {
      throw new CodegenException(
          "Endpoint rules of "
              + service.getId()
              + " use the function "
              + id
              + ", which no C# integration implements.");
    }
    return function;
  }

  /** The auth trait shape id for an endpoint {@code authSchemes} name. */
  public String authSchemeId(String name) {
    return authSchemeIds.getOrDefault(name, name);
  }

  /** Endpoint {@code authSchemes} names mapped to auth trait shape ids. */
  public Map<String, String> authSchemeIds() {
    return authSchemeIds;
  }

  /** Rule parameters supplied by client configuration properties, in declaration order. */
  public List<ConfigParameter> configParameters() {
    return configParameters;
  }

  /** The parameter bound to {@code SDK::Endpoint}, if the rules declare one. */
  public Optional<Parameter> endpointParameter() {
    return endpointParameter;
  }

  public static String parametersTypeName(ServiceShape service) {
    return CSharpNaming.typeName(service.getId().getName()) + "EndpointParameters";
  }

  public static String resolverTypeName(ServiceShape service) {
    return CSharpNaming.typeName(service.getId().getName()) + "EndpointResolver";
  }
}

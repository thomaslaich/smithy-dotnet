/*
 * Declares a service's client in Client.g.fs:
 *
 *   {Service}Error         one case per error the service declares, when it declares any
 *   {Operation}Error       one case per error the operation declares, and a Service case holding
 *                          a {Service}Error, when the service declares errors
 *   I{Service}Client       one Task-returning method per operation, and IDisposable
 *   {Service}ClientConfig  the runtime's SmithyClientConfig
 *   {Service}Client        binds each operation to the service's default protocol once, when it
 *                          is constructed, and calls the runtime's SmithyClientRuntime
 *
 * The runtime does the work (serialization, transport, retries, auth); the client only passes it
 * the operation's binding, schema, and input. An operation whose model declares errors, on the
 * operation or on the service, returns a Result with those errors in the Error case; every other
 * failure stays an exception.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ProtocolInfo;
import io.github.thomaslaich.nsmithy.codegen.core.Protocols;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.ServiceIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.EndpointTrait;
import software.amazon.smithy.model.traits.IdempotencyTokenTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class ClientGenerator {

  private static final String CLIENT = "global.NSmithy.Client.";
  private static final String RESULT = "global.Microsoft.FSharp.Core.Result";
  private static final String SERVICE_CASE = "Service";
  private static final String TASKS = "global.System.Threading.Tasks.";
  private static final String CANCELLATION_TOKEN = "global.System.Threading.CancellationToken";

  private final FSharpContext context;
  private final Model model;
  private final FSharpSymbolProvider symbols;
  private final ServiceShape service;
  private final SchemaGenerator schemas;

  ClientGenerator(FSharpContext context, ServiceShape service) {
    this.context = context;
    this.model = context.model();
    this.symbols = context.symbolProvider();
    this.service = service;
    this.schemas = new SchemaGenerator(context);
  }

  /** The protocols a client can use: the built-in ones, then each integration's. */
  static List<ProtocolInfo> registeredProtocols(FSharpContext context) {
    List<ProtocolInfo> protocols = new ArrayList<>(Protocols.BUILT_IN);
    context.integrations().forEach(integration -> protocols.addAll(integration.protocols()));
    return Protocols.registered(protocols);
  }

  void run() {
    String name = FSharpNaming.typeName(service.getId().getName());
    String client = name + "Client";
    String config = client + "Config";
    String clientInterface = "I" + client;
    List<OperationShape> operations =
        TopDownIndex.of(model).getContainedOperations(service).stream()
            .sorted(Comparator.comparing(operation -> operation.getId().toString()))
            .toList();
    List<String> generatedNames = new ArrayList<>(List.of(client, config, clientInterface));
    if (!serviceErrors().isEmpty()) {
      generatedNames.add(serviceErrorTypeName());
    }
    operations.stream()
        .filter(this::returnsResult)
        .forEach(operation -> generatedNames.add(errorTypeName(operation)));
    GeneratedNames.check(model, service, generatedNames.toArray(String[]::new));

    FSharpWriter writer = context.files().client(service);
    writeServiceErrorType(writer);
    operations.forEach(operation -> writeErrorType(writer, operation));
    writeInterface(writer, clientInterface, operations);

    // A service with no protocol the client knows gets the interface only.
    Optional<ProtocolInfo> protocol =
        Protocols.clientDefault(
            registeredProtocols(context), context.settings().defaultProtocol(), service);
    if (protocol.isEmpty()) {
      return;
    }
    writeConfig(writer, config);
    writeClient(writer, client, config, clientInterface, operations, protocol.get());
  }

  /**
   * The union of the service's errors, which every operation can return. One type, so that a caller
   * handles them in one place and passes them on as one value.
   */
  private void writeServiceErrorType(FSharpWriter writer) {
    List<StructureShape> errors = serviceErrors();
    if (errors.isEmpty()) {
      return;
    }
    List<String> cases =
        errors.stream()
            .map(error -> "| " + errorCaseName(error) + " of " + symbols.typeOf(error))
            .toList();
    writer.writeTemplate(
        """
        /// <summary>The errors the model declares for every operation of <c>${service:L}</c>.</summary>
        [<RequireQualifiedAccess>]
        type ${type:L} =
            ${#cases}
            ${value:L}
            ${/cases}

        """,
        Map.of(
            "service",
            FSharpNaming.typeName(service.getId().getName()),
            "type",
            serviceErrorTypeName(),
            "cases",
            cases));
  }

  /**
   * The union of an operation's errors: its own, and the service's behind one Service case. Its
   * cases are qualified, so they do not hide the error types they are named after, and operations
   * can share an error.
   */
  private void writeErrorType(FSharpWriter writer, OperationShape operation) {
    if (!returnsResult(operation)) {
      return;
    }
    List<String> cases = new ArrayList<>();
    for (StructureShape error : ownErrors(operation)) {
      if (errorCaseName(error).equals(SERVICE_CASE)) {
        throw new CodegenException(
            "Error "
                + error.getId()
                + " of operation "
                + operation.getId()
                + " would be the case "
                + SERVICE_CASE
                + " of "
                + errorTypeName(operation)
                + ", which holds the errors of "
                + service.getId()
                + ". Rename the error, for example with the renameShapes build transform.");
      }
      cases.add("| " + errorCaseName(error) + " of " + symbols.typeOf(error));
    }
    if (!serviceErrors().isEmpty()) {
      cases.add("| " + SERVICE_CASE + " of " + serviceErrorTypeName());
    }
    writer.writeTemplate(
        """
        /// <summary>The errors the model declares for <c>${method:L}</c>.</summary>
        [<RequireQualifiedAccess>]
        type ${type:L} =
            ${#cases}
            ${value:L}
            ${/cases}

        """,
        Map.of("method", methodName(operation), "type", errorTypeName(operation), "cases", cases));
  }

  private void writeInterface(
      FSharpWriter writer, String clientInterface, List<OperationShape> operations) {
    List<String> methods = new ArrayList<>();
    for (OperationShape operation : operations) {
      methods.add(
          FSharpWriter.documented(
              FSharpWriter.docs(operation),
              "abstract "
                  + methodName(operation)
                  + ": "
                  + String.join(" * ", parameterTypes(operation))
                  + " -> "
                  + returnType(operation)));
    }
    writer.writeTemplate(
        """
        ${?docs}
        ${docs:L|}
        ${/docs}
        type ${interface:L} =
            inherit global.System.IDisposable
            ${#methods}

            ${value:L|}
            ${/methods}
        """,
        Map.of(
            "docs", FSharpWriter.docs(service),
            "interface", clientInterface,
            "methods", methods));
  }

  private void writeConfig(FSharpWriter writer, String config) {
    writer.writeTemplate(
        """
        /// <summary>Options for the client. It copies them when it is constructed.</summary>
        [<Sealed>]
        type ${config:L} =
            inherit global.NSmithy.Client.SmithyClientConfig
            new() = { inherit global.NSmithy.Client.SmithyClientConfig() }
            new(source: ${config:L}) = { inherit global.NSmithy.Client.SmithyClientConfig(source) }
        """,
        Map.of("config", config));
  }

  private void writeClient(
      FSharpWriter writer,
      String client,
      String config,
      String clientInterface,
      List<OperationShape> operations,
      ProtocolInfo protocol) {
    String serviceSchema = FSharpSymbolProvider.render(symbols.schemaSymbol(service)) + ".Schema";
    List<String> forwards = new ArrayList<>();
    for (OperationShape operation : operations) {
      forwards.add(
          "member this."
              + methodName(operation)
              + "("
              + parameterNames(operation)
              + ") = this."
              + methodName(operation)
              + "("
              + forwardedArguments(operation)
              + ")");
    }
    Map<String, Object> context = new HashMap<>();
    context.put("docs", FSharpWriter.docs(service));
    context.put("client", client);
    context.put("interface", clientInterface);
    context.put("authSchemes", strings(modeledAuthSchemes()));
    context.put(
        "idempotency",
        operations.stream().anyMatch(operation -> !idempotencyTokens(operation).isEmpty()));
    context.put("bindings", writer.consumer(w -> writeBindings(w, serviceSchema, operations)));
    context.put(
        "constructors",
        writer.consumer(w -> writeConstructors(w, client, config, serviceSchema, protocol)));
    context.put(
        "operations",
        writer.consumer(w ->
            operations.forEach(operation -> {
              w.write("");
              writeOperation(w, operation);
            })));
    context.put("forwards", forwards);
    writer.writeTemplate(
        """
        ${?docs}
        ${docs:L|}
        ${/docs}
        [<Sealed>]
        type ${client:L} private (parts: global.NSmithy.Client.SmithyHttpClientEnvironment * global.NSmithy.Client.SmithyClientConfig) =
            static let modeledAuthSchemes: global.System.Collections.Generic.IReadOnlyList<string> = ${authSchemes:L}
            let environment, config = parts
            let runtime = environment.Runtime
            ${?idempotency}
            let idempotencyToken =
                match global.Microsoft.FSharp.Core.Option.ofObj config.IdempotencyTokenProvider with
                | Some provider -> fun () -> provider.Invoke()
                | None -> fun () -> global.System.Guid.NewGuid().ToString()
            ${/idempotency}
            ${bindings:C|}

            ${constructors:C|}
            ${operations:C|}

            member _.Dispose() = environment.Dispose()

            interface ${interface:L} with
                member _.Dispose() = environment.Dispose()
                ${#forwards}
                ${value:L}
                ${/forwards}
        """,
        context);
  }

  /**
   * Binds every operation once. When binding fails, the environment the client was given is
   * disposed, since no client exists to dispose it.
   */
  private void writeBindings(
      FSharpWriter writer, String serviceSchema, List<OperationShape> operations) {
    List<String> bindings = new ArrayList<>();
    for (OperationShape operation : operations.stream().filter(this::canBind).toList()) {
      String operationSchema =
          FSharpSymbolProvider.render(symbols.schemaSymbol(operation)) + ".Schema";
      List<String> arguments = new ArrayList<>();
      arguments.add(serviceSchema + ".Id");
      arguments.add(operationSchema + ".Id");
      arguments.add("serviceProtocol.ForClientOperation(" + operationSchema + ")");
      arguments.add("authSchemeIds = " + strings(operationAuthSchemes(operation)));
      hostPrefix(operation).ifPresent(prefix -> arguments.add("hostPrefix = " + prefix));
      arguments.add("operation = " + operationSchema);
      bindings.add(
          FSharpWriter.render(
              """
              ${name:L} =
                  global.NSmithy.Client.SmithyOperationBinding<${input:L}, ${output:L}>(
                      ${arguments:L|})\
              """,
              Map.of(
                  "name", FSharpNaming.typeName(operation.getId().getName()),
                  "input", schemas.operationType(operation.getInputShape()),
                  "output", schemas.operationType(operation.getOutputShape()),
                  "arguments", String.join(",\n", arguments))));
    }
    if (bindings.isEmpty()) {
      return;
    }
    writer.writeTemplate(
        """
        let serviceProtocol = environment.ServiceProtocol
        let bindings =
            try
                {|
                    ${#bindings}
                    ${value:L|}
                    ${/bindings}
                |}
            with _ ->
                environment.Dispose()
                reraise ()\
        """,
        Map.of("bindings", bindings));
  }

  private void writeConstructors(
      FSharpWriter writer,
      String client,
      String config,
      String serviceSchema,
      ProtocolInfo protocol) {
    writer.writeTemplate(
        """
        /// <summary>A client that sends requests to <c>endpoint</c>.</summary>
        new(endpoint: global.System.Uri, ?config: ${config:L}) = new ${client:L}(${client:L}.WithEndpoint(endpoint, config))

        /// <summary>A client that owns its HTTP client, configured by <c>config</c>.</summary>
        new(config: ${config:L}) = new ${client:L}(${client:L}.Owned(config))

        /// <summary>A client that sends requests with <c>httpClient</c>, which it does not dispose.</summary>
        new(httpClient: global.System.Net.Http.HttpClient, ?config: ${config:L}) =
            new ${client:L}(${client:L}.WithHttpClient(httpClient, config))

        /// <summary>A client that runs on an existing runtime, which owns the transport.</summary>
        new(runtime: global.NSmithy.Client.SmithyClientRuntime, ?config: ${config:L}) =
            new ${client:L}(${client:L}.WithRuntime(runtime, config))

        static member private Owned(config: ${config:L}) : global.NSmithy.Client.SmithyHttpClientEnvironment * global.NSmithy.Client.SmithyClientConfig =
            global.System.ArgumentNullException.ThrowIfNull(config)
            ${create:L}), config :> global.NSmithy.Client.SmithyClientConfig

        static member private WithEndpoint(endpoint: global.System.Uri, config: ${config:L} option) : ${config:L} =
            global.System.ArgumentNullException.ThrowIfNull(endpoint)
            let copy =
                match config with
                | Some config -> ${config:L}(config)
                | None -> ${config:L}()
            copy.Endpoint <- endpoint
            copy

        static member private WithHttpClient(httpClient: global.System.Net.Http.HttpClient, config: ${config:L} option) : global.NSmithy.Client.SmithyHttpClientEnvironment * global.NSmithy.Client.SmithyClientConfig =
            global.System.ArgumentNullException.ThrowIfNull(httpClient)
            let config = global.Microsoft.FSharp.Core.Option.defaultWith (fun () -> ${config:L}()) config
            ${create:L}, httpClient = httpClient), config :> global.NSmithy.Client.SmithyClientConfig

        static member private WithRuntime(runtime: global.NSmithy.Client.SmithyClientRuntime, config: ${config:L} option) : global.NSmithy.Client.SmithyHttpClientEnvironment * global.NSmithy.Client.SmithyClientConfig =
            let config = global.Microsoft.FSharp.Core.Option.defaultWith (fun () -> ${config:L}()) config
            global.NSmithy.Client.SmithyHttpClientEnvironment.FromRuntime(${serviceSchema:L}, runtime, config, ${protocolFactory:L}),
            config :> global.NSmithy.Client.SmithyClientConfig\
        """,
        Map.of(
            "config", config,
            "client", client,
            "serviceSchema", serviceSchema,
            "protocolFactory", protocolFactory(protocol),
            "create",
                FSharpWriter.render(
                    "global.NSmithy.Client.SmithyHttpClientEnvironment.Create(${serviceSchema:L},"
                        + " config, ${protocolFactory:L}, modeledAuthSchemes${?httpVersion},"
                        + " modeledHttpVersion = ${httpVersion:L}${/httpVersion}",
                    Map.of(
                        "serviceSchema", serviceSchema,
                        "protocolFactory", protocolFactory(protocol),
                        "httpVersion", httpVersionPreference(protocol).orElse("")))));
  }

  private static String protocolFactory(ProtocolInfo protocol) {
    return "global.System.Func<global.NSmithy.Http.IProtocol>(fun () -> global."
        + protocol.runtimeTypeName()
        + "() :> global.NSmithy.Http.IProtocol)";
  }

  private void writeOperation(FSharpWriter writer, OperationShape operation) {
    boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
    Map<String, Object> context = new HashMap<>();
    context.put("docs", FSharpWriter.docs(operation));
    context.put("method", methodName(operation));
    context.put("parameters", typedParameters(operation));
    context.put("returns", returnType(operation));
    context.put("canBind", canBind(operation));
    context.put("hasInput", hasInput);
    context.put(
        "tokens", idempotencyTokens(operation).stream().map(FSharpNaming::fieldName).toList());
    context.put(
        "invoke",
        "runtime.InvokeAsync(bindings."
            + FSharpNaming.typeName(operation.getId().getName())
            + ", "
            + (hasInput ? "input" : "global.NSmithy.Core.SmithyUnit.Value")
            + ", global.Microsoft.FSharp.Core.Operators.defaultArg cancellationToken"
            + " global.System.Threading.CancellationToken.None)");
    context.put("unitOutput", ShapeSupport.isUnit(operation.getOutputShape()));
    context.put("returnsResult", returnsResult(operation));
    context.put("errorClauses", errorClauses(operation));
    context.put("result", RESULT);
    writer.writeTemplate(
        """
        ${?docs}
        ${docs:L|}
        ${/docs}
        member _.${method:L}(${parameters:L}) : ${returns:L} =
            ${^canBind}
            raise (global.System.NotSupportedException("Event-stream operations are not supported by the declared service protocols."))
            ${/canBind}
            ${?canBind}
            ${?hasInput}
            global.System.ArgumentNullException.ThrowIfNull(input)
            ${/hasInput}
            ${?tokens}
            let input =
                { input with
                    ${#tokens}
                    ${value:L} = (match input.${value:L} with Some _ as token -> token | None -> Some(idempotencyToken ()))
                    ${/tokens}
                }
            ${/tokens}
            ${?returnsResult}
            task {
                try
                    ${?unitOutput}
                    let! _ = ${invoke:L}
                    return ${result:L}.Ok()
                    ${/unitOutput}
                    ${^unitOutput}
                    let! output = ${invoke:L}
                    return ${result:L}.Ok output
                    ${/unitOutput}
                with
                ${#errorClauses}
                ${value:L}
                ${/errorClauses}
            }
            ${/returnsResult}
            ${^returnsResult}
            ${invoke:L}${?unitOutput} :> global.System.Threading.Tasks.Task${/unitOutput}
            ${/returnsResult}
            ${/canBind}\
        """,
        context);
  }

  private String methodName(OperationShape operation) {
    return FSharpNaming.typeName(operation.getId().getName()) + "Async";
  }

  private List<String> parameterTypes(OperationShape operation) {
    List<String> parameters = new ArrayList<>();
    if (!ShapeSupport.isUnit(operation.getInputShape())) {
      parameters.add("input: " + schemas.operationType(operation.getInputShape()));
    }
    parameters.add("?cancellationToken: " + CANCELLATION_TOKEN);
    return parameters;
  }

  private String typedParameters(OperationShape operation) {
    return String.join(", ", parameterTypes(operation));
  }

  private String parameterNames(OperationShape operation) {
    return ShapeSupport.isUnit(operation.getInputShape())
        ? "?cancellationToken"
        : "input, ?cancellationToken";
  }

  private String forwardedArguments(OperationShape operation) {
    return ShapeSupport.isUnit(operation.getInputShape())
        ? "?cancellationToken = cancellationToken"
        : "input, ?cancellationToken = cancellationToken";
  }

  /**
   * The match clauses that turn a caught error into the operation's Error case: the operation's own
   * errors as their cases, then the service's behind the Service case. An error declared on both is
   * the operation's.
   */
  private List<String> errorClauses(OperationShape operation) {
    List<String> clauses = new ArrayList<>();
    List<StructureShape> own = ownErrors(operation);
    String type = errorTypeName(operation);
    for (StructureShape error : own) {
      clauses.add(errorClause(error, type + "." + errorCaseName(error) + " error"));
    }
    for (StructureShape error : serviceErrors()) {
      if (!own.contains(error)) {
        String serviceCase = serviceErrorTypeName() + "." + errorCaseName(error) + " error";
        clauses.add(errorClause(error, type + "." + SERVICE_CASE + "(" + serviceCase + ")"));
      }
    }
    return clauses;
  }

  /** Catches the error's exception and returns it as {@code value}, the operation's Error case. */
  private String errorClause(StructureShape error, String value) {
    return "| :? "
        + symbols.typeOf(error)
        + " as error -> return "
        + RESULT
        + ".Error("
        + value
        + ")";
  }

  private String returnType(OperationShape operation) {
    String output = schemas.operationType(operation.getOutputShape());
    if (returnsResult(operation)) {
      String value = ShapeSupport.isUnit(operation.getOutputShape()) ? "unit" : output;
      return TASKS + "Task<" + RESULT + "<" + value + ", " + errorTypeName(operation) + ">>";
    }
    return ShapeSupport.isUnit(operation.getOutputShape())
        ? TASKS + "Task"
        : TASKS + "Task<" + output + ">";
  }

  /** Whether the operation returns a Result: its model declares errors for it or its service. */
  private boolean returnsResult(OperationShape operation) {
    return !ownErrors(operation).isEmpty() || !serviceErrors().isEmpty();
  }

  /** The errors the operation itself declares, in a stable order. */
  private List<StructureShape> ownErrors(OperationShape operation) {
    return errorShapes(operation.getErrors());
  }

  /** The errors the service declares for every operation, in a stable order. */
  private List<StructureShape> serviceErrors() {
    return errorShapes(service.getErrors());
  }

  private List<StructureShape> errorShapes(List<ShapeId> errors) {
    return errors.stream()
        .sorted()
        .map(error -> model.expectShape(error, StructureShape.class))
        .toList();
  }

  private String errorTypeName(OperationShape operation) {
    return FSharpNaming.typeName(operation.getId().getName()) + "Error";
  }

  private String serviceErrorTypeName() {
    return FSharpNaming.typeName(service.getId().getName()) + "Error";
  }

  private String errorCaseName(StructureShape error) {
    return FSharpNaming.typeName(service.getContextualName(error));
  }

  /** An event-stream operation needs a protocol that binds event streams. */
  private boolean canBind(OperationShape operation) {
    if (!ShapeSupport.isEventStreamOperation(model, operation)) {
      return true;
    }
    return Protocols.ofService(registeredProtocols(context), service).stream()
        .anyMatch(ProtocolInfo::eventStreams);
  }

  /**
   * The members an operation fills in with a new idempotency token when the caller leaves them out.
   */
  private List<MemberShape> idempotencyTokens(OperationShape operation) {
    if (ShapeSupport.isUnit(operation.getInputShape())) {
      return List.of();
    }
    StructureShape input = model.expectShape(operation.getInputShape(), StructureShape.class);
    return input.members().stream()
        .filter(member -> member.hasTrait(IdempotencyTokenTrait.class))
        .filter(FSharpMembers::isOptional)
        .toList();
  }

  /**
   * Every auth scheme that can be effective for an operation of the service: the service's, then
   * those operation-level {@code @auth} overrides introduce.
   */
  private List<String> modeledAuthSchemes() {
    ServiceIndex index = ServiceIndex.of(model);
    Set<String> ids = new LinkedHashSet<>();
    index.getEffectiveAuthSchemes(service).keySet().forEach(id -> ids.add(id.toString()));
    TopDownIndex.of(model).getContainedOperations(service).stream()
        .sorted(Comparator.comparing(operation -> operation.getId().toString()))
        .forEach(operation ->
            index
                .getEffectiveAuthSchemes(service, operation)
                .keySet()
                .forEach(id -> ids.add(id.toString())));
    return List.copyOf(ids);
  }

  /** The operation's effective auth schemes, in the order the runtime tries them. */
  private List<String> operationAuthSchemes(OperationShape operation) {
    return ServiceIndex.of(model).getEffectiveAuthSchemes(service, operation).keySet().stream()
        .map(ShapeId::toString)
        .toList();
  }

  /** Expands an operation's {@code @endpoint} host prefix from its input. */
  private Optional<String> hostPrefix(OperationShape operation) {
    Optional<EndpointTrait> endpoint = operation.getTrait(EndpointTrait.class);
    if (endpoint.isEmpty()) {
      return Optional.empty();
    }
    StructureShape input = model.expectShape(operation.getInputShape(), StructureShape.class);
    String labels =
        endpoint.get().getHostPrefix().getLabels().stream()
            .map(label -> {
              MemberShape member = input.getMember(label.getContent()).orElseThrow();
              return CLIENT
                  + "SmithyHostLabel("
                  + FSharpNaming.stringLiteral(label.getContent())
                  + ", input."
                  + FSharpNaming.fieldName(member)
                  + ")";
            })
            .collect(Collectors.joining("; ", "[| ", " |]"));
    String type = schemas.operationType(operation.getInputShape());
    return Optional.of(
        "global.System.Func<"
            + type
            + ", string>(fun (input: "
            + type
            + ") -> "
            + CLIENT
            + "SmithyHostPrefix.Expand("
            + FSharpNaming.stringLiteral(endpoint.get().getHostPrefix().toString())
            + ", global.System.ReadOnlySpan<"
            + CLIENT
            + "SmithyHostLabel>("
            + labels.replace("[|  |]", "[||]")
            + ")))");
  }

  private Optional<String> httpVersionPreference(ProtocolInfo protocol) {
    return Protocols.httpVersionPreference(
            service, protocol.trait(), Protocols.hasEventStreamOperations(model, service))
        .map(preference ->
            "global.NSmithy.Http.SmithyHttpVersionPreference(global.System.Net.HttpVersion."
                + switch (preference.alpnId()) {
                  case "h3" -> "Version30";
                  case "h2" -> "Version20";
                  default -> "Version11";
                }
                + ", "
                + preference.allowDowngrade()
                + ")");
  }

  private static String strings(List<String> values) {
    return values.isEmpty()
        ? "global.System.Array.Empty<string>()"
        : values.stream()
            .map(FSharpNaming::stringLiteral)
            .collect(Collectors.joining("; ", "[| ", " |]"));
  }
}

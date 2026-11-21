/*
 * Declares a service's client in Client.g.fs:
 *
 *   I{Service}Client       one Task-returning method per operation, and IDisposable
 *   I{Service}ClientExtensions
 *                          overloads that leave out the input, and paginators for the
 *                          operations modeled with @paginated
 *   {Service}ClientConfig  the runtime's SmithyClientConfig
 *   {Service}Client        binds each operation to the service's default protocol once, when it
 *                          is constructed, and calls the runtime's SmithyClientRuntime
 *
 * The runtime does the work (serialization, transport, retries, auth); the client only passes it
 * the operation's binding, schema, and input. An operation that can return modeled errors, its own
 * or its service's, returns a Result with its error union (see OperationHandlerGenerator) in the
 * Error case. Every other failure stays an exception.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.Paginators;
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
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginationInfo;
import software.amazon.smithy.model.knowledge.ServiceIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.ListShape;
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

  /** Reserves the names of types the service's generated code declares in its namespace. */
  static void reserve(FSharpContext context, ServiceShape service, List<String> names) {
    String namespace = context.settings().fsharpNamespace(service.getId().getNamespace());
    for (String name : names) {
      context
          .generatedNames()
          .reserve(namespace, name, "the generated code for " + service.getId() + " declares");
    }
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
    if (operations.stream().anyMatch(this::hasExtensions)) {
      generatedNames.add(clientInterface + "Extensions");
    }
    reserve(context, service, generatedNames);

    FSharpWriter writer = context.files().client(service);
    writeInterface(writer, clientInterface, operations);
    writeInterfaceOverloads(writer, clientInterface, operations);

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
              extensions(operation)
                  .forEach(member -> {
                    w.write("");
                    w.writeTemplate("${member:L|}", Map.of("member", member));
                  });
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

  /**
   * Whether a caller may leave the input out: every member of the input is optional or has a
   * default, so an empty input is a valid one, and none of them is a stream, which the caller must
   * supply. Such an operation also gets an overload that takes only the cancellation token.
   */
  private boolean inputIsOmittable(OperationShape operation) {
    if (ShapeSupport.isUnit(operation.getInputShape())) {
      return false;
    }
    StructureShape input = model.expectShape(operation.getInputShape(), StructureShape.class);
    return input.members().stream()
        .allMatch(member ->
            (FSharpMembers.isOptional(member) || FSharpMembers.usesDefault(member))
                && !ShapeSupport.isStreamingBlobMember(model, member)
                && !ShapeSupport.isEventStreamMember(model, member));
  }

  /**
   * The empty input of an omittable operation: every optional member absent, every default used.
   */
  private String emptyInput(OperationShape operation) {
    StructureShape input = model.expectShape(operation.getInputShape(), StructureShape.class);
    String type = schemas.operationType(operation.getInputShape());
    if (input.members().isEmpty()) {
      return type + "()";
    }
    String fields =
        input.members().stream()
            .map(member ->
                FSharpNaming.fieldName(member)
                    + " = "
                    + (FSharpMembers.usesDefault(member)
                        ? FSharpLiterals.defaultValue(context, member)
                        : "None"))
            .collect(Collectors.joining("; "));
    return "({ " + fields + " } : " + type + ")";
  }

  /** Whether the operation has members built on it: an overload, or paginators. */
  private boolean hasExtensions(OperationShape operation) {
    return inputIsOmittable(operation) || pagination(operation).isPresent();
  }

  /**
   * The members built on an operation: an overload without the input when it may be left out, and
   * paginators when it is modeled with {@code @paginated}. The client declares them, and so does an
   * extension of its interface, which every implementation of it gets.
   */
  private List<String> extensions(OperationShape operation) {
    List<String> members = new ArrayList<>();
    if (inputIsOmittable(operation)) {
      members.add(
          inputlessOverload(
              FSharpWriter.docs(operation),
              methodName(operation),
              returnType(operation),
              operation));
    }
    pagination(operation).ifPresent(info -> members.addAll(paginators(info)));
    return members;
  }

  /** The overload of an omittable operation's method that takes only the cancellation token. */
  private String inputlessOverload(
      String docs, String method, String returns, OperationShape operation) {
    Map<String, Object> values = new HashMap<>();
    values.put("docs", docs);
    values.put("method", method);
    values.put("cancellationToken", CANCELLATION_TOKEN);
    values.put("returns", returns);
    values.put("input", emptyInput(operation));
    return FSharpWriter.render(
        """
        ${?docs}
        ${docs:L|}
        ${/docs}
        member this.${method:L}(?cancellationToken: ${cancellationToken:L}) : ${returns:L} =
            this.${method:L}(${input:L}, ?cancellationToken = cancellationToken)\
        """,
        values);
  }

  /**
   * The operation's resolved {@code @paginated} trait, when it gets paginators: its tokens must be
   * strings, which the runtime pages with.
   */
  private Optional<PaginationInfo> pagination(OperationShape operation) {
    return Paginators.of(model, service, operation)
        .filter(info -> Paginators.hasStringTokens(model, info));
  }

  /**
   * The pages paginator, which calls the operation again with each page's continuation token, and,
   * when the trait names a list of items, the items paginator, which flattens the pages. When the
   * operation returns a Result, so does every element, and an Error is the last one.
   */
  private List<String> paginators(PaginationInfo info) {
    List<String> members = new ArrayList<>();
    OperationShape operation = info.getOperation();
    String name = FSharpNaming.typeName(operation.getId().getName());
    String page = pageType(operation);
    String inputType = schemas.operationType(operation.getInputShape());
    MemberShape inputToken = info.getInputTokenMember();
    String pagesMethod = name + "PagesAsync";
    String pages = asyncEnumerable(page);
    String pagesDocs =
        summary(
            "Every page of <c>"
                + methodName(operation)
                + "</c>, which is called again with each page's continuation token until a page"
                + " has none."
                + (returnsResult(operation) ? " A modeled error is the last page." : ""));

    Map<String, Object> values = new HashMap<>();
    values.put("docs", pagesDocs);
    values.put("method", pagesMethod);
    values.put("operation", methodName(operation));
    values.put("input", inputType);
    values.put("cancellationToken", CANCELLATION_TOKEN);
    values.put("returns", pages);
    values.put("page", page);
    values.put("nextToken", onOutput(operation, "null", tokenExpression(info)));
    values.put("tokenField", FSharpNaming.fieldName(inputToken));
    values.put("token", FSharpMembers.isOptional(inputToken) ? "Some token" : "token");
    members.add(
        FSharpWriter.render(
            """
            ${docs:L|}
            member this.${method:L}(input: ${input:L}, ?cancellationToken: ${cancellationToken:L}) : ${returns:L} =
                global.System.ArgumentNullException.ThrowIfNull(input)
                global.NSmithy.Client.SmithyPaginator.PagesAsync(
                    (fun (input: ${input:L}) (cancellationToken: ${cancellationToken:L}) -> this.${operation:L}(input, cancellationToken)),
                    input,
                    (fun (page: ${page:L}) -> ${nextToken:L}),
                    (fun (input: ${input:L}) (token: string) -> { input with ${tokenField:L} = ${token:L} }),
                    global.Microsoft.FSharp.Core.Operators.defaultArg cancellationToken global.System.Threading.CancellationToken.None)\
            """,
            values));
    if (inputIsOmittable(operation)) {
      members.add(inputlessOverload(pagesDocs, pagesMethod, pages, operation));
    }

    Optional<ListShape> list = Paginators.items(model, info);
    if (list.isEmpty()) {
      return members;
    }
    String element = symbols.elementOf(list.get());
    String item = returnsResult(operation) ? result(element, operation) : element;
    String itemsMethod = name + "ItemsAsync";
    String items = asyncEnumerable(item);
    String itemList = memberPath("output", info.getItemsMemberPath());
    String itemSeq =
        "global.Microsoft.FSharp.Collections.Seq.ofList ("
            + (isOptionPath(info.getItemsMemberPath())
                ? "global.Microsoft.FSharp.Core.Option.defaultValue [] (" + itemList + ")"
                : itemList)
            + ")";

    String itemsDocs =
        summary(
            "The items of every page of <c>"
                + methodName(operation)
                + "</c>."
                + (returnsResult(operation) ? " A modeled error is the last item." : ""));

    Map<String, Object> itemValues = new HashMap<>(values);
    itemValues.put("docs", itemsDocs);
    itemValues.put("method", itemsMethod);
    itemValues.put("pages", pagesMethod);
    itemValues.put("returns", items);
    itemValues.put("item", item);
    itemValues.put(
        "items",
        returnsResult(operation)
            ? "match page with\n| "
                + RESULT
                + ".Ok output -> global.Microsoft.FSharp.Collections.Seq.map "
                + RESULT
                + ".Ok ("
                + itemSeq
                + ")\n| "
                + RESULT
                + ".Error error -> global.Microsoft.FSharp.Collections.Seq.singleton ("
                + RESULT
                + ".Error error)"
            : "let output = page\n" + itemSeq);
    members.add(
        FSharpWriter.render(
            """
            ${docs:L|}
            member this.${method:L}(input: ${input:L}, ?cancellationToken: ${cancellationToken:L}) : ${returns:L} =
                global.System.Linq.AsyncEnumerable.SelectMany(
                    this.${pages:L}(input, ?cancellationToken = cancellationToken),
                    global.System.Func<${page:L}, ${item:L} seq>(fun page ->
                        ${items:L|}))\
            """,
            itemValues));
    if (inputIsOmittable(operation)) {
      members.add(inputlessOverload(itemsDocs, itemsMethod, items, operation));
    }
    return members;
  }

  /** A page: the operation's output, or the Result it returns. */
  private String pageType(OperationShape operation) {
    String output = schemas.operationType(operation.getOutputShape());
    return returnsResult(operation) ? result(output, operation) : output;
  }

  private String result(String value, OperationShape operation) {
    return RESULT
        + "<"
        + value
        + ", "
        + FSharpSymbolProvider.render(symbols.errorsSymbol(operation).orElseThrow())
        + ">";
  }

  private static String asyncEnumerable(String element) {
    return "global.System.Collections.Generic.IAsyncEnumerable<" + element + ">";
  }

  private static String summary(String text) {
    return "/// <summary>" + text + "</summary>";
  }

  /**
   * Evaluates {@code expression} on a page's output, bound to {@code output}; on an Error, which
   * has none, it is {@code otherwise}.
   */
  private String onOutput(OperationShape operation, String otherwise, String expression) {
    if (!returnsResult(operation)) {
      return "let output = page in " + expression;
    }
    return "match page with "
        + RESULT
        + ".Ok output -> "
        + expression
        + " | "
        + RESULT
        + ".Error _ -> "
        + otherwise;
  }

  /** The page's continuation token, a string that is null after the last page. */
  private String tokenExpression(PaginationInfo info) {
    String token = memberPath("output", info.getOutputTokenMemberPath());
    return isOptionPath(info.getOutputTokenMemberPath())
        ? "global.Microsoft.FSharp.Core.Option.toObj (" + token + ")"
        : token;
  }

  /**
   * Reads a member path from {@code root}. Once a member is an option, the rest of the path reads
   * through it, and the result is an option.
   */
  private static String memberPath(String root, List<MemberShape> path) {
    String expression = root;
    boolean option = false;
    for (MemberShape member : path) {
      String field = FSharpNaming.fieldName(member);
      if (!option) {
        expression = expression + "." + field;
        option = FSharpMembers.isOptional(member);
      } else {
        expression =
            expression
                + " |> global.Microsoft.FSharp.Core.Option."
                + (FSharpMembers.isOptional(member) ? "bind" : "map")
                + " (fun value -> value."
                + field
                + ")";
      }
    }
    return expression;
  }

  private static boolean isOptionPath(List<MemberShape> path) {
    return path.stream().anyMatch(FSharpMembers::isOptional);
  }

  /** The members built on the operations on the interface, as extension members. */
  private void writeInterfaceOverloads(
      FSharpWriter writer, String clientInterface, List<OperationShape> operations) {
    List<OperationShape> extended = operations.stream().filter(this::hasExtensions).toList();
    if (extended.isEmpty()) {
      return;
    }
    Map<String, Object> values = new HashMap<>();
    values.put("interface", clientInterface);
    values.put(
        "members",
        String.join(
            "\n\n",
            extended.stream().flatMap(operation -> extensions(operation).stream()).toList()));
    writer.writeTemplate(
        """
        /// <summary>Members of <c>${interface:L}</c> built on its operations.</summary>
        [<AutoOpen>]
        module ${interface:L}Extensions =
            type ${interface:L} with
                ${members:L|}

        """,
        values);
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
   * The match clause that turns a caught error into the operation's Error case. The operation's
   * union maps the exception itself, so a client of an operation another project generated uses
   * that project's cases.
   */
  private List<String> errorClauses(OperationShape operation) {
    Optional<Symbol> errors = symbols.errorsSymbol(operation);
    if (errors.isEmpty()) {
      return List.of();
    }
    String from =
        "("
            + FSharpSymbolProvider.render(errors.get())
            + "."
            + OperationHandlerGenerator.fromExceptionMethod()
            + " error)";
    return List.of(
        "| error when " + from + ".IsSome -> return " + RESULT + ".Error(" + from + ".Value)");
  }

  private String returnType(OperationShape operation) {
    String output = schemas.operationType(operation.getOutputShape());
    Optional<Symbol> errors = symbols.errorsSymbol(operation);
    if (errors.isPresent()) {
      String value = ShapeSupport.isUnit(operation.getOutputShape()) ? "unit" : output;
      return TASKS
          + "Task<"
          + RESULT
          + "<"
          + value
          + ", "
          + FSharpSymbolProvider.render(errors.get())
          + ">>";
    }
    return ShapeSupport.isUnit(operation.getOutputShape())
        ? TASKS + "Task"
        : TASKS + "Task<" + output + ">";
  }

  /** Whether the operation returns a Result: it can return modeled errors. */
  private boolean returnsResult(OperationShape operation) {
    return symbols.errorsSymbol(operation).isPresent();
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

/*
 * Declares a service's client in Client.g.fs:
 *
 *   I{Service}Client       one Task-returning method per operation, and IDisposable
 *   {Service}ClientConfig  the runtime's SmithyClientConfig
 *   {Service}Client        binds each operation to the service's default protocol once, when it
 *                          is constructed, and calls the runtime's SmithyClientRuntime
 *
 * The runtime does the work (serialization, transport, retries, auth); the client only passes it
 * the operation's binding, schema, and input.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ProtocolInfo;
import io.github.thomaslaich.nsmithy.codegen.core.Protocols;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
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
    checkNames(client, config, clientInterface);
    List<OperationShape> operations =
        TopDownIndex.of(model).getContainedOperations(service).stream()
            .sorted(Comparator.comparing(operation -> operation.getId().toString()))
            .toList();

    FSharpWriter writer = context.files().client(service);
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

  private void writeInterface(
      FSharpWriter writer, String clientInterface, List<OperationShape> operations) {
    writer.writeDocs(service);
    writer.write("type $L =", clientInterface).indent();
    writer.write("inherit global.System.IDisposable");
    for (OperationShape operation : operations) {
      writer.write("");
      writer.writeDocs(operation);
      writer.write(
          "abstract $L: $L -> $L",
          methodName(operation),
          String.join(" * ", parameterTypes(operation)),
          returnType(operation));
    }
    writer.dedent().write("");
  }

  private void writeConfig(FSharpWriter writer, String config) {
    writer.write(
        """
        /// <summary>Options for the client. It copies them when it is constructed.</summary>
        [<Sealed>]
        type $1L =
            inherit $2LSmithyClientConfig
            new() = { inherit $2LSmithyClientConfig() }
            new(source: $1L) = { inherit $2LSmithyClientConfig(source) }
        """,
        config,
        CLIENT);
  }

  private void writeClient(
      FSharpWriter writer,
      String client,
      String config,
      String clientInterface,
      List<OperationShape> operations,
      ProtocolInfo protocol) {
    String serviceSchema = FSharpSymbolProvider.render(symbols.schemaSymbol(service)) + ".Schema";
    String protocolFactory =
        "global.System.Func<global.NSmithy.Http.IProtocol>(fun () -> global."
            + protocol.runtimeTypeName()
            + "() :> global.NSmithy.Http.IProtocol)";
    boolean needsIdempotency =
        operations.stream().anyMatch(operation -> !idempotencyTokens(operation).isEmpty());

    writer.writeDocs(service);
    writer.write("[<Sealed>]");
    writer.write(
        "type $L private (parts: $LSmithyHttpClientEnvironment * $LSmithyClientConfig) =",
        client,
        CLIENT,
        CLIENT);
    writer.indent();
    writer.write(
        "static let modeledAuthSchemes: global.System.Collections.Generic.IReadOnlyList<string> ="
            + " $L",
        strings(modeledAuthSchemes()));
    writer.write("let environment, config = parts");
    writer.write("let runtime = environment.Runtime");
    if (needsIdempotency) {
      writer.write(
          """
          let idempotencyToken =
              match global.Microsoft.FSharp.Core.Option.ofObj config.IdempotencyTokenProvider with
              | Some provider -> fun () -> provider.Invoke()
              | None -> fun () -> global.System.Guid.NewGuid().ToString()\
          """);
    }
    writeBindings(writer, serviceSchema, operations);
    writer.write("");

    writeConstructors(writer, client, config, serviceSchema, protocolFactory, protocol);

    for (OperationShape operation : operations) {
      writer.write("");
      writeOperation(writer, operation);
    }
    writer.write("");
    writer.write("member _.Dispose() = environment.Dispose()");
    writer.write("");
    writer.write("interface $L with", clientInterface).indent();
    writer.write("member _.Dispose() = environment.Dispose()");
    for (OperationShape operation : operations) {
      String forward = forwardedArguments(operation);
      writer.write(
          "member this.$L($L) = this.$L($L)",
          methodName(operation),
          parameterNames(operation),
          methodName(operation),
          forward);
    }
    writer.dedent().dedent().write("");
  }

  /**
   * Binds every operation once. When binding fails, the environment the client was given is
   * disposed, since no client exists to dispose it.
   */
  private void writeBindings(
      FSharpWriter writer, String serviceSchema, List<OperationShape> operations) {
    List<OperationShape> bound = operations.stream().filter(this::canBind).toList();
    if (bound.isEmpty()) {
      return;
    }
    writer.write("let serviceProtocol = environment.ServiceProtocol");
    writer.write("let bindings =").indent();
    writer.write("try").indent();
    writer.write("{|").indent();
    for (OperationShape operation : bound) {
      String operationSchema =
          FSharpSymbolProvider.render(symbols.schemaSymbol(operation)) + ".Schema";
      List<String> arguments = new ArrayList<>();
      arguments.add(serviceSchema + ".Id");
      arguments.add(operationSchema + ".Id");
      arguments.add("serviceProtocol.ForClientOperation(" + operationSchema + ")");
      arguments.add("authSchemeIds = " + strings(operationAuthSchemes(operation)));
      hostPrefix(operation).ifPresent(prefix -> arguments.add("hostPrefix = " + prefix));
      arguments.add("operation = " + operationSchema);
      writer.write("$L =", FSharpNaming.typeName(operation.getId().getName()));
      writer.indent();
      writer.write(
          "$LSmithyOperationBinding<$L, $L>(",
          CLIENT,
          schemas.operationType(operation.getInputShape()),
          schemas.operationType(operation.getOutputShape()));
      writer.indent().write("$L)", String.join(",\n", arguments)).dedent().dedent();
    }
    writer.dedent().write("|}").dedent();
    writer.write("with _ ->").indent();
    writer.write("environment.Dispose()");
    writer.write("reraise ()").dedent().dedent();
  }

  private void writeConstructors(
      FSharpWriter writer,
      String client,
      String config,
      String serviceSchema,
      String protocolFactory,
      ProtocolInfo protocol) {
    String create =
        CLIENT
            + "SmithyHttpClientEnvironment.Create("
            + serviceSchema
            + ", config, "
            + protocolFactory
            + ", modeledAuthSchemes"
            + httpVersionPreference(protocol).map(p -> ", modeledHttpVersion = " + p).orElse("");
    writer.write(
        """
        /// <summary>A client that sends requests to <c>endpoint</c>.</summary>
        new(endpoint: global.System.Uri, ?config: $1L) = new $2L($2L.WithEndpoint(endpoint, config))

        /// <summary>A client that owns its HTTP client, configured by <c>config</c>.</summary>
        new(config: $1L) = new $2L($2L.Owned(config))

        /// <summary>A client that sends requests with <c>httpClient</c>, which it does not dispose.</summary>
        new(httpClient: global.System.Net.Http.HttpClient, ?config: $1L) =
            new $2L($2L.WithHttpClient(httpClient, config))

        /// <summary>A client that runs on an existing runtime, which owns the transport.</summary>
        new(runtime: $3LSmithyClientRuntime, ?config: $1L) =
            new $2L($2L.WithRuntime(runtime, config))

        static member private Owned(config: $1L) : $3LSmithyHttpClientEnvironment * $3LSmithyClientConfig =
            global.System.ArgumentNullException.ThrowIfNull(config)
            $4L), config :> $3LSmithyClientConfig

        static member private WithEndpoint(endpoint: global.System.Uri, config: $1L option) : $1L =
            global.System.ArgumentNullException.ThrowIfNull(endpoint)
            let copy =
                match config with
                | Some config -> $1L(config)
                | None -> $1L()
            copy.Endpoint <- endpoint
            copy

        static member private WithHttpClient(httpClient: global.System.Net.Http.HttpClient, config: $1L option) : $3LSmithyHttpClientEnvironment * $3LSmithyClientConfig =
            global.System.ArgumentNullException.ThrowIfNull(httpClient)
            let config = global.Microsoft.FSharp.Core.Option.defaultWith (fun () -> $1L()) config
            $4L, httpClient = httpClient), config :> $3LSmithyClientConfig

        static member private WithRuntime(runtime: $3LSmithyClientRuntime, config: $1L option) : $3LSmithyHttpClientEnvironment * $3LSmithyClientConfig =
            let config = global.Microsoft.FSharp.Core.Option.defaultWith (fun () -> $1L()) config
            $3LSmithyHttpClientEnvironment.FromRuntime($5L, runtime, config, $6L),
            config :> $3LSmithyClientConfig\
        """,
        config,
        client,
        CLIENT,
        create,
        serviceSchema,
        protocolFactory);
  }

  private void writeOperation(FSharpWriter writer, OperationShape operation) {
    writer.writeDocs(operation);
    writer.write(
        "member _.$L($L) : $L =",
        methodName(operation),
        typedParameters(operation),
        returnType(operation));
    writer.indent();
    if (!canBind(operation)) {
      writer.write(
          "raise (global.System.NotSupportedException(\"Event-stream operations are not supported"
              + " by the declared service protocols.\"))");
      writer.dedent();
      return;
    }
    boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
    String input = hasInput ? "input" : "global.NSmithy.Core.SmithyUnit.Value";
    if (hasInput) {
      writer.write("global.System.ArgumentNullException.ThrowIfNull(input)");
      List<MemberShape> tokens = idempotencyTokens(operation);
      if (!tokens.isEmpty()) {
        writer.write("let input =").indent().write("{ input with").indent();
        for (MemberShape token : tokens) {
          String field = FSharpNaming.fieldName(token);
          writer.write(
              "$1L = (match input.$1L with Some _ as token -> token | None -> Some(idempotencyToken"
                  + " ()))",
              field);
        }
        writer.dedent().write("}").dedent();
      }
    }
    String call =
        "runtime.InvokeAsync(bindings."
            + FSharpNaming.typeName(operation.getId().getName())
            + ", "
            + input
            + ", global.Microsoft.FSharp.Core.Operators.defaultArg cancellationToken "
            + CANCELLATION_TOKEN
            + ".None)";
    writer.write(
        ShapeSupport.isUnit(operation.getOutputShape()) ? "$L :> " + TASKS + "Task" : "$L", call);
    writer.dedent();
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

  private String returnType(OperationShape operation) {
    return ShapeSupport.isUnit(operation.getOutputShape())
        ? TASKS + "Task"
        : TASKS + "Task<" + schemas.operationType(operation.getOutputShape()) + ">";
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
        .forEach(
            operation ->
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
            .map(
                label -> {
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
        .map(
            preference ->
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

  /** The client's names cannot clash with a model type of the service's namespace. */
  private void checkNames(String... names) {
    String namespace = service.getId().getNamespace();
    for (String name : names) {
      model
          .shapes()
          .filter(shape -> !shape.isMemberShape())
          .filter(shape -> shape.getId().getNamespace().equals(namespace))
          .filter(shape -> FSharpNaming.typeName(shape.getId().getName()).equals(name))
          .findFirst()
          .ifPresent(
              shape -> {
                throw new CodegenException(
                    "Shape "
                        + shape.getId()
                        + " would generate an F# type named "
                        + name
                        + ", which the client of "
                        + service.getId()
                        + " declares. Rename the shape, for example with the renameShapes build"
                        + " transform.");
              });
    }
  }

  private static String strings(List<String> values) {
    return values.isEmpty()
        ? "global.System.Array.Empty<string>()"
        : values.stream()
            .map(FSharpNaming::stringLiteral)
            .collect(Collectors.joining("; ", "[| ", " |]"));
  }
}

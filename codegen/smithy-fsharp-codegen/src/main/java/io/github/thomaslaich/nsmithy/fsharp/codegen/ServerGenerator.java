/*
 * Declares a service's server in Server.g.fs:
 *
 *   I{Operation}Handler          one method per operation, which an application implements
 *   I{Service}ServiceHandler     every operation's handler
 *   {Service}ServiceProtocols    the protocols the service is served over, as flags
 *   {Service}ServiceDefinition   the operation table: each operation's schema, its handler
 *                                resolved from a request's services, its JSON Schemas, and
 *                                its auth schemes
 *   {Service}ServiceServerExtensions
 *                                Add{Service}ServiceHandler and Map{Service}Service, extension
 *                                methods for ASP.NET Core; Map{Service}Service returns the
 *                                service's route group
 *
 * The runtime's SmithyServerRuntime reads requests, validates input, calls the handler, and writes
 * responses and errors; the generated code only tells it which handler serves which operation.
 */
package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.JsonSchemas;
import io.github.thomaslaich.nsmithy.codegen.core.ProtocolInfo;
import io.github.thomaslaich.nsmithy.codegen.core.Protocols;
import io.github.thomaslaich.nsmithy.codegen.core.ServicePrompts;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.ServiceIndex;
import software.amazon.smithy.model.knowledge.ServiceIndex.AuthSchemeMode;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.synthetic.NoAuthTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class ServerGenerator {

  private static final String SERVER = "global.NSmithy.Server.";
  private static final String TASK = "global.System.Threading.Tasks.Task";
  private static final String CANCELLATION_TOKEN = "global.System.Threading.CancellationToken";

  private final FSharpContext context;
  private final Model model;
  private final FSharpSymbolProvider symbols;
  private final ServiceShape service;
  private final SchemaGenerator schemas;

  ServerGenerator(FSharpContext context, ServiceShape service) {
    this.context = context;
    this.model = context.model();
    this.symbols = context.symbolProvider();
    this.service = service;
    this.schemas = new SchemaGenerator(context);
  }

  void run() {
    String name = FSharpNaming.typeName(service.getId().getName());
    String contract = name.endsWith("Service") ? name : name + "Service";
    List<OperationShape> operations =
        TopDownIndex.of(model).getContainedOperations(service).stream()
            .sorted(Comparator.comparing(operation -> operation.getId().toString()))
            .toList();
    List<ProtocolInfo> protocols =
        Protocols.ofService(ClientGenerator.registeredProtocols(context), service).stream()
            .filter(ProtocolInfo::server)
            .toList();

    List<String> names =
        new ArrayList<>(
            List.of(
                "I" + contract + "Handler",
                contract + "Protocols",
                contract + "Definition",
                contract + "ServerExtensions"));
    ClientGenerator.reserve(context, service, names);

    FSharpWriter writer = context.files().server(service);
    writeServiceHandler(writer, contract, operations);
    if (!protocols.isEmpty()) {
      writeProtocols(writer, contract, protocols);
    }
    writeDefinition(writer, contract, operations);
    writeExtensions(writer, contract, operations, protocols);
  }

  private void writeServiceHandler(
      FSharpWriter writer, String contract, List<OperationShape> operations) {
    writer.writeTemplate(
        """
        ${?docs}
        ${docs:L|}
        ${/docs}
        type I${contract:L}Handler =
            ${^handlers}
            interface end
            ${/handlers}
            ${#handlers}
            inherit ${value:L}
            ${/handlers}
        """,
        Map.of(
            "docs", FSharpWriter.docs(service),
            "contract", contract,
            "handlers", operations.stream().map(this::handlerName).toList()));
  }

  private void writeProtocols(FSharpWriter writer, String contract, List<ProtocolInfo> protocols) {
    Map<String, Integer> flags = new LinkedHashMap<>();
    for (int i = 0; i < protocols.size(); i++) {
      flags.put(protocols.get(i).name(), 1 << i);
    }
    writer.writeTemplate(
        """
        /// <summary>The protocols the service can be served over.</summary>
        [<global.System.Flags>]
        type ${contract:L}Protocols =
            | None = 0
            ${#flags}
            | ${key:L} = ${value:L}
            ${/flags}
            | All = ${all:L}
        """,
        Map.of("contract", contract, "flags", flags, "all", (1 << protocols.size()) - 1));
  }

  private void writeDefinition(
      FSharpWriter writer, String contract, List<OperationShape> operations) {
    String serviceSchema = FSharpSymbolProvider.render(symbols.schemaSymbol(service)) + ".Schema";
    List<String> catalog = new ArrayList<>();
    catalog.add(serviceSchema);
    operations.forEach(operation -> catalog.add(serverOperation(operation)));
    writer.writeTemplate(
        """
        /// <summary>The service's operation table, shared by every host adapter.</summary>
        [<Sealed>]
        type ${contract:L}Definition() =
            /// A handler's Result as the runtime expects it: the output, or a faulted task that
            /// carries the error's exception, which the runtime writes without throwing it.
            static let settle
                (pending: global.System.Threading.Tasks.Task<global.Microsoft.FSharp.Core.Result<'Value, 'Error>>)
                (output: 'Value -> 'Output)
                (error: 'Error -> global.System.Exception)
                : global.System.Threading.Tasks.Task<'Output> =
                let complete result =
                    match result with
                    | global.Microsoft.FSharp.Core.Result.Ok value -> global.System.Threading.Tasks.Task.FromResult(output value)
                    | global.Microsoft.FSharp.Core.Result.Error failure -> global.System.Threading.Tasks.Task.FromException<'Output>(error failure)

                if pending.IsCompletedSuccessfully then
                    complete pending.Result
                else
                    global.System.Threading.Tasks.TaskExtensions.Unwrap(task { let! result = pending in return complete result })

            static let catalog =
                global.NSmithy.Server.ServiceOperationCatalog(
                    ${catalog:L|})
            static let prompts: global.System.Collections.Generic.IReadOnlyList<global.NSmithy.Server.ServicePromptDefinition> = ${prompts:L}

            static member internal Catalog = catalog

            interface global.NSmithy.Server.IServiceDefinition with
                member _.Schema = ${serviceSchema:L}
                member _.Prompts = prompts
                member _.Operations = catalog
        """,
        Map.of(
            "contract",
            contract,
            "catalog",
            String.join(",\n", catalog),
            "prompts",
            prompts(operations),
            "serviceSchema",
            serviceSchema));
  }

  /**
   * An operation table entry: the operation's schema, a function that resolves its handler from a
   * request's services, for an operation that streams nothing, its JSON Schemas, and its auth
   * schemes.
   */
  private String serverOperation(OperationShape operation) {
    boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
    String input = schemas.operationType(operation.getInputShape());
    String output = schemas.operationType(operation.getOutputShape());
    String call =
        "handler."
            + methodName(operation)
            + "("
            + (hasInput ? "input, " : "")
            + "cancellationToken)";
    return FSharpWriter.render(
        """
        global.NSmithy.Server.ServerOperation.Create<${input:L}, ${output:L}>(
            ${schema:L},
            global.System.Func<global.System.IServiceProvider, ${delegate:L}>(fun services ->
                let handler = global.Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<${handler:L}>(services)
                ${delegate:L}(fun ${inputName:L} cancellationToken -> ${body:L}))${?jsonSchemas},
            jsonSchemas = ${jsonSchemas:L}${/jsonSchemas}${?authSchemes},
            authSchemes = ${authSchemes:L}${/authSchemes}) :> global.NSmithy.Server.IServerOperation\
        """,
        Map.of(
            "input",
            input,
            "output",
            output,
            "schema",
            FSharpSymbolProvider.render(symbols.schemaSymbol(operation)) + ".Schema",
            "delegate",
            "global.System.Func<"
                + input
                + ", "
                + CANCELLATION_TOKEN
                + ", "
                + TASK
                + "<"
                + output
                + ">>",
            "handler",
            handlerName(operation),
            "inputName",
            hasInput ? "input" : "_",
            "body",
            body(operation, call),
            "jsonSchemas",
            isStreaming(operation) ? "" : jsonSchemas(operation),
            "authSchemes",
            authSchemes(operation).orElse("")));
  }

  /**
   * The call to the handler, as the runtime's delegate returns it: a Unit output as the runtime's
   * SmithyUnit, and a Result settled into the output or the error's exception, without throwing it.
   */
  private String body(OperationShape operation, String call) {
    boolean unit = ShapeSupport.isUnit(operation.getOutputShape());
    Optional<Symbol> errors = symbols.errorsSymbol(operation);
    if (errors.isPresent()) {
      return "settle "
          + "("
          + call
          + ") "
          + (unit
              ? "(fun () -> global.NSmithy.Core.SmithyUnit.Value)"
              : "global.Microsoft.FSharp.Core.Operators.id")
          + " (fun (error: "
          + FSharpSymbolProvider.render(errors.get())
          + ") -> error."
          + OperationHandlerGenerator.toExceptionMethod()
          + "())";
    }
    return unit ? "task { do! " + call + "; return global.NSmithy.Core.SmithyUnit.Value }" : call;
  }

  private String jsonSchemas(OperationShape operation) {
    List<String> arguments = new ArrayList<>();
    arguments.add(
        FSharpNaming.stringLiteral(JsonSchemas.generate(model, operation.getInputShape())));
    arguments.add(
        FSharpNaming.stringLiteral(JsonSchemas.generate(model, operation.getOutputShape())));
    operation
        .getTrait(DocumentationTrait.class)
        .ifPresent(trait -> arguments.add(FSharpNaming.stringLiteral(trait.getValue())));
    return SERVER + "OperationJsonSchemas(" + String.join(", ", arguments) + ")";
  }

  /**
   * The operation's effective auth schemes, with {@code smithy.api#noAuth} when it can be invoked
   * without authentication. Empty when that is the only scheme, the runtime's default.
   */
  private Optional<String> authSchemes(OperationShape operation) {
    List<ShapeId> schemes =
        List.copyOf(
            ServiceIndex.of(model)
                .getEffectiveAuthSchemes(service, operation, AuthSchemeMode.NO_AUTH_AWARE)
                .keySet());
    if (schemes.equals(List.of(NoAuthTrait.ID))) {
      return Optional.empty();
    }
    return Optional.of(
        schemes.stream()
            .map(FSharpLiterals::shapeId)
            .collect(Collectors.joining("; ", "[| ", " |]")));
  }

  private String prompts(List<OperationShape> operations) {
    List<ServicePrompts.Prompt> prompts = ServicePrompts.of(model, service, operations);
    if (prompts.isEmpty()) {
      return "global.System.Array.Empty<" + SERVER + "ServicePromptDefinition>()";
    }
    return prompts.stream()
        .map(prompt -> {
          List<String> arguments = new ArrayList<>();
          arguments.add(FSharpNaming.stringLiteral(prompt.name()));
          arguments.add(FSharpNaming.stringLiteral(prompt.description()));
          arguments.add(FSharpNaming.stringLiteral(prompt.template()));
          if (prompt.preferWhen() != null) {
            arguments.add("preferWhen = " + FSharpNaming.stringLiteral(prompt.preferWhen()));
          }
          if (!prompt.arguments().isEmpty()) {
            arguments.add(
                "arguments = "
                    + prompt.arguments().stream()
                        .map(argument ->
                            SERVER
                                + "ServicePromptArgumentDefinition("
                                + FSharpNaming.stringLiteral(argument.name())
                                + ", "
                                + (argument.description() == null
                                    ? "null"
                                    : FSharpNaming.stringLiteral(argument.description()))
                                + ", "
                                + argument.required()
                                + ")")
                        .collect(Collectors.joining("; ", "[| ", " |]")));
          }
          return SERVER + "ServicePromptDefinition(" + String.join(", ", arguments) + ")";
        })
        .collect(Collectors.joining("; ", "[| ", " |]"));
  }

  private void writeExtensions(
      FSharpWriter writer,
      String contract,
      List<OperationShape> operations,
      List<ProtocolInfo> protocols) {
    List<String> handlers = new ArrayList<>(List.of("I" + contract + "Handler"));
    operations.forEach(operation -> handlers.add(handlerName(operation)));
    Map<String, String> runtimeTypes = new LinkedHashMap<>();
    protocols.forEach(protocol -> runtimeTypes.put(protocol.name(), protocol.runtimeTypeName()));
    writer.writeTemplate(
        """
        /// <summary>Registers and maps the service in ASP.NET Core.</summary>
        [<global.System.Runtime.CompilerServices.Extension>]
        [<AbstractClass; Sealed>]
        type ${contract:L}ServerExtensions =
            /// <summary>Registers the service's definition.</summary>
            [<global.System.Runtime.CompilerServices.Extension>]
            static member Add${contract:L}(services: global.Microsoft.Extensions.DependencyInjection.IServiceCollection) : global.Microsoft.Extensions.DependencyInjection.IServiceCollection =
                global.System.ArgumentNullException.ThrowIfNull(services)
                global.Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(services, global.Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global.NSmithy.Server.IServiceDefinition, ${contract:L}Definition>())
                services

            /// <summary>Registers <c>'THandler</c>, once per <c>lifetime</c> (by default a singleton), as the handler of every operation, and the service.</summary>
            [<global.System.Runtime.CompilerServices.Extension>]
            static member Add${contract:L}Handler<'THandler when 'THandler : not struct and 'THandler : not null and 'THandler :> I${contract:L}Handler>(services: global.Microsoft.Extensions.DependencyInjection.IServiceCollection, ?lifetime: global.Microsoft.Extensions.DependencyInjection.ServiceLifetime) : global.Microsoft.Extensions.DependencyInjection.IServiceCollection =
                global.System.ArgumentNullException.ThrowIfNull(services)
                let lifetime = global.Microsoft.FSharp.Core.Operators.defaultArg lifetime global.Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton
                ${?protocols}
                global.NSmithy.Server.AspNetCore.SmithyServerServiceCollectionExtensions.AddSmithyServer(services) |> ignore
                ${/protocols}
                ${contract:L}ServerExtensions.Add${contract:L}(services) |> ignore
                services.Add(global.Microsoft.Extensions.DependencyInjection.ServiceDescriptor(global.Microsoft.FSharp.Core.Operators.typeof<'THandler>, global.Microsoft.FSharp.Core.Operators.typeof<'THandler>, lifetime))
                ${#handlers}
                services.Add(global.Microsoft.Extensions.DependencyInjection.ServiceDescriptor(global.Microsoft.FSharp.Core.Operators.typeof<${value:L}>, global.System.Func<global.System.IServiceProvider, obj>(fun provider -> global.Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<'THandler>(provider) :> obj), lifetime))
                ${/handlers}
                services

            /// <summary>Registers <c>handler</c>, such as an object expression, as the handler of every operation, and the service.</summary>
            [<global.System.Runtime.CompilerServices.Extension>]
            static member Add${contract:L}Handler(services: global.Microsoft.Extensions.DependencyInjection.IServiceCollection, handler: I${contract:L}Handler) : global.Microsoft.Extensions.DependencyInjection.IServiceCollection =
                global.System.ArgumentNullException.ThrowIfNull(services)
                global.System.ArgumentNullException.ThrowIfNull(handler)
                ${?protocols}
                global.NSmithy.Server.AspNetCore.SmithyServerServiceCollectionExtensions.AddSmithyServer(services) |> ignore
                ${/protocols}
                ${contract:L}ServerExtensions.Add${contract:L}(services) |> ignore
                global.Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<I${contract:L}Handler>(services, handler) |> ignore
                ${#operationHandlers}
                global.Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<${value:L}>(services, handler :> ${value:L}) |> ignore
                ${/operationHandlers}
                services

            /// <summary>Registers the handler <c>factory</c> creates from the container, once per <c>lifetime</c> (by default a singleton), as the handler of every operation, and the service.</summary>
            [<global.System.Runtime.CompilerServices.Extension>]
            static member Add${contract:L}Handler(services: global.Microsoft.Extensions.DependencyInjection.IServiceCollection, factory: global.System.IServiceProvider -> I${contract:L}Handler, ?lifetime: global.Microsoft.Extensions.DependencyInjection.ServiceLifetime) : global.Microsoft.Extensions.DependencyInjection.IServiceCollection =
                global.System.ArgumentNullException.ThrowIfNull(services)
                let lifetime = global.Microsoft.FSharp.Core.Operators.defaultArg lifetime global.Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton
                ${?protocols}
                global.NSmithy.Server.AspNetCore.SmithyServerServiceCollectionExtensions.AddSmithyServer(services) |> ignore
                ${/protocols}
                ${contract:L}ServerExtensions.Add${contract:L}(services) |> ignore
                services.Add(global.Microsoft.Extensions.DependencyInjection.ServiceDescriptor(global.Microsoft.FSharp.Core.Operators.typeof<I${contract:L}Handler>, global.System.Func<global.System.IServiceProvider, obj>(fun provider -> factory provider :> obj), lifetime))
                ${#operationHandlers}
                services.Add(global.Microsoft.Extensions.DependencyInjection.ServiceDescriptor(global.Microsoft.FSharp.Core.Operators.typeof<${value:L}>, global.System.Func<global.System.IServiceProvider, obj>(fun provider -> global.Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<I${contract:L}Handler>(provider) :> obj), lifetime))
                ${/operationHandlers}
                services
            ${?protocols}

            /// <summary>Maps the service's operations over <c>protocols</c>, by default its first protocol, into a route group, and returns the group.</summary>
            [<global.System.Runtime.CompilerServices.Extension>]
            static member Map${contract:L}(endpoints: global.Microsoft.AspNetCore.Routing.IEndpointRouteBuilder, ?protocols: ${contract:L}Protocols) : global.Microsoft.AspNetCore.Routing.RouteGroupBuilder =
                global.System.ArgumentNullException.ThrowIfNull(endpoints)
                let protocols = global.Microsoft.FSharp.Core.Operators.defaultArg protocols ${contract:L}Protocols.${defaultProtocol:L}
                if (protocols &&& ~~~${contract:L}Protocols.All) <> ${contract:L}Protocols.None then
                    raise (global.System.ArgumentOutOfRangeException("protocols", box protocols, "Unknown ${contract:L}Protocols value."))
                let selected = global.System.Collections.Generic.List<global.NSmithy.Server.AspNetCore.SmithyServerProtocol>()
                ${#protocols}
                if (protocols &&& ${contract:L}Protocols.${key:L}) <> ${contract:L}Protocols.None then
                    selected.Add(global.NSmithy.Server.AspNetCore.SmithyServerProtocol("${key:L}", global.${value:L}()))
                ${/protocols}
                global.NSmithy.Server.AspNetCore.SmithyAspNetCoreServer.MapSmithyService(endpoints, ${contract:L}Definition.Catalog, selected)
            ${/protocols}
        """,
        Map.of(
            "contract", contract,
            "handlers", handlers,
            "operationHandlers", handlers.subList(1, handlers.size()),
            "protocols", runtimeTypes,
            "defaultProtocol", protocols.isEmpty() ? "" : protocols.get(0).name()));
  }

  private boolean isStreaming(OperationShape operation) {
    return ShapeSupport.isStreamingShape(model, operation.getInputShape())
        || ShapeSupport.isStreamingShape(model, operation.getOutputShape());
  }

  /** An operation's handler interface, qualified: it lives in the operation's namespace. */
  private String handlerName(OperationShape operation) {
    return FSharpSymbolProvider.render(symbols.handlerSymbol(operation));
  }

  private static String methodName(OperationShape operation) {
    return OperationHandlerGenerator.methodName(operation);
  }
}

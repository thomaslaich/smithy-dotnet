/*
 * Declares a service's server in Server.g.fs:
 *
 *   I{Operation}Handler          one method per operation, which an application implements
 *   I{Service}ServiceHandler     every operation's handler
 *   {Service}ServiceProtocols    the protocols the service is served over, as flags
 *   {Service}ServiceDefinition   the operation table: each operation's schema, its handler
 *                                resolved from a request's services, and its JSON Schemas
 *   {Service}ServiceServerExtensions
 *                                Add{Service}ServiceHandler and Map{Service}Service, extension
 *                                methods for ASP.NET Core
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
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
final class ServerGenerator {

  private static final String SERVER = "global.NSmithy.Server.";
  private static final String ASP_NET_CORE = "global.NSmithy.Server.AspNetCore.";
  private static final String DI = "global.Microsoft.Extensions.DependencyInjection.";
  private static final String EXTENSION = "[<global.System.Runtime.CompilerServices.Extension>]";
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

    List<String> names = new ArrayList<>();
    operations.forEach(operation -> names.add(handlerName(operation)));
    names.addAll(
        List.of(
            "I" + contract + "Handler",
            contract + "Protocols",
            contract + "Definition",
            contract + "ServerExtensions"));
    GeneratedNames.check(model, service, names.toArray(String[]::new));

    FSharpWriter writer = context.files().server(service);
    for (OperationShape operation : operations) {
      writeHandler(writer, operation);
    }
    writeServiceHandler(writer, contract, operations);
    if (!protocols.isEmpty()) {
      writeProtocols(writer, contract, protocols);
    }
    writeDefinition(writer, contract, operations);
    writeExtensions(writer, contract, operations, protocols);
  }

  private void writeHandler(FSharpWriter writer, OperationShape operation) {
    writer.writeDocs(operation);
    writer.write("type $L =", handlerName(operation)).indent();
    writer.writeDocs(operation);
    List<String> parameters = new ArrayList<>();
    if (!ShapeSupport.isUnit(operation.getInputShape())) {
      parameters.add("input: " + schemas.operationType(operation.getInputShape()));
    }
    parameters.add("cancellationToken: " + CANCELLATION_TOKEN);
    writer.write(
        "abstract $L: $L -> $L",
        methodName(operation),
        String.join(" * ", parameters),
        ShapeSupport.isUnit(operation.getOutputShape())
            ? TASK
            : TASK + "<" + schemas.operationType(operation.getOutputShape()) + ">");
    writer.dedent().write("");
  }

  private void writeServiceHandler(
      FSharpWriter writer, String contract, List<OperationShape> operations) {
    writer.writeDocs(service);
    writer.write("type I$LHandler =", contract).indent();
    if (operations.isEmpty()) {
      writer.write("interface end");
    }
    operations.forEach(operation -> writer.write("inherit $L", handlerName(operation)));
    writer.dedent().write("");
  }

  private void writeProtocols(FSharpWriter writer, String contract, List<ProtocolInfo> protocols) {
    writer.write("/// <summary>The protocols the service can be served over.</summary>");
    writer.write("[<global.System.Flags>]");
    writer.write("type $LProtocols =", contract).indent();
    writer.write("| None = 0");
    for (int i = 0; i < protocols.size(); i++) {
      writer.write("| $L = $L", protocols.get(i).name(), 1 << i);
    }
    writer.write("| All = $L", (1 << protocols.size()) - 1);
    writer.dedent().write("");
  }

  private void writeDefinition(
      FSharpWriter writer, String contract, List<OperationShape> operations) {
    String serviceSchema = FSharpSymbolProvider.render(symbols.schemaSymbol(service)) + ".Schema";
    writer.write(
        "/// <summary>The service's operation table, shared by every host adapter.</summary>");
    writer.write("[<Sealed>]");
    writer.write("type $LDefinition() =", contract).indent();
    writer.write("static let catalog =").indent();
    writer.write("$LServiceOperationCatalog(", SERVER).indent();
    List<String> arguments = new ArrayList<>();
    arguments.add(serviceSchema);
    for (OperationShape operation : operations) {
      arguments.add(serverOperation(operation));
    }
    writer.write("$L)", String.join(",\n", arguments)).dedent().dedent();
    writer.write(
        "static let prompts:"
            + " global.System.Collections.Generic.IReadOnlyList<$LServicePromptDefinition> = $L",
        SERVER,
        prompts(operations));
    writer.write("");
    writer.write("static member internal Catalog = catalog");
    writer.write("");
    writer.write("interface $LIServiceDefinition with", SERVER).indent();
    writer.write("member _.Schema = $L", serviceSchema);
    writer.write("member _.Prompts = prompts");
    writer.write("member _.Operations = catalog");
    writer.dedent().dedent().write("");
  }

  /**
   * An operation table entry: the operation's schema, a function that resolves its handler from a
   * request's services, and, for an operation that streams nothing, its JSON Schemas.
   */
  private String serverOperation(OperationShape operation) {
    boolean hasInput = !ShapeSupport.isUnit(operation.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(operation.getOutputShape());
    String input = schemas.operationType(operation.getInputShape());
    String output = schemas.operationType(operation.getOutputShape());
    String delegate =
        "global.System.Func<"
            + input
            + ", "
            + CANCELLATION_TOKEN
            + ", "
            + TASK
            + "<"
            + output
            + ">>";
    String call =
        "handler."
            + methodName(operation)
            + "("
            + (hasInput ? "input, " : "")
            + "cancellationToken)";
    String body =
        hasOutput ? call : "task { do! " + call + "; return global.NSmithy.Core.SmithyUnit.Value }";
    String resolve =
        "global.System.Func<global.System.IServiceProvider, "
            + delegate
            + ">(fun services ->\n    let handler = "
            + DI
            + "ServiceProviderServiceExtensions.GetRequiredService<"
            + handlerName(operation)
            + ">(services)\n    "
            + delegate
            + "(fun "
            + (hasInput ? "input" : "_")
            + " cancellationToken -> "
            + body
            + "))";
    List<String> arguments = new ArrayList<>();
    arguments.add(FSharpSymbolProvider.render(symbols.schemaSymbol(operation)) + ".Schema");
    arguments.add(resolve);
    if (!isStreaming(operation)) {
      arguments.add("jsonSchemas = " + jsonSchemas(operation));
    }
    return SERVER
        + "ServerOperation.Create<"
        + input
        + ", "
        + output
        + ">(\n    "
        + String.join(",\n", arguments).replace("\n", "\n    ")
        + ") :> "
        + SERVER
        + "IServerOperation";
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

  private String prompts(List<OperationShape> operations) {
    List<ServicePrompts.Prompt> prompts = ServicePrompts.of(model, service, operations);
    if (prompts.isEmpty()) {
      return "global.System.Array.Empty<" + SERVER + "ServicePromptDefinition>()";
    }
    return prompts.stream()
        .map(
            prompt -> {
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
                            .map(
                                argument ->
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
    String services = DI + "IServiceCollection";
    String handler = "I" + contract + "Handler";
    writer.write("/// <summary>Registers and maps the service in ASP.NET Core.</summary>");
    writer.write(EXTENSION);
    writer.write("[<AbstractClass; Sealed>]");
    writer.write("type $LServerExtensions =", contract).indent();

    writer.write("/// <summary>Registers the service's definition.</summary>");
    writer.write(EXTENSION);
    writer.write("static member Add$L(services: $L) : $L =", contract, services, services).indent();
    writer.write("global.System.ArgumentNullException.ThrowIfNull(services)");
    writer.write(
        "$LExtensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(services,"
            + " $LServiceDescriptor.Singleton<$LIServiceDefinition, $LDefinition>())",
        DI,
        DI,
        SERVER,
        contract);
    writer.write("services").dedent();
    writer.write("");

    writer.write(
        "/// <summary>Registers <c>'THandler</c> as the handler of every operation, and the"
            + " service.</summary>");
    writer.write(EXTENSION);
    writer.write(
        "static member Add$LHandler<'THandler when 'THandler : not struct and 'THandler : not null"
            + " and 'THandler :> $L>(services: $L) : $L =",
        contract,
        handler,
        services,
        services);
    writer.indent();
    writer.write("global.System.ArgumentNullException.ThrowIfNull(services)");
    if (!protocols.isEmpty()) {
      writer.write(
          "$LSmithyServerServiceCollectionExtensions.AddSmithyServer(services) |> ignore",
          ASP_NET_CORE);
    }
    writer.write("$LServerExtensions.Add$L(services) |> ignore", contract, contract);
    writer.write(
        "$LServiceCollectionServiceExtensions.AddSingleton<'THandler>(services) |> ignore", DI);
    List<String> registered = new ArrayList<>(List.of(handler));
    operations.forEach(operation -> registered.add(handlerName(operation)));
    for (String type : registered) {
      writer.write(
          "$1LServiceCollectionServiceExtensions.AddSingleton<$2L>(services,"
              + " global.System.Func<global.System.IServiceProvider, $2L>(fun provider ->"
              + " $1LServiceProviderServiceExtensions.GetRequiredService<'THandler>(provider) :>"
              + " $2L)) |> ignore",
          DI,
          type);
    }
    writer.write("services").dedent();

    if (!protocols.isEmpty()) {
      String protocolEnum = contract + "Protocols";
      String routes = "global.Microsoft.AspNetCore.Routing.IEndpointRouteBuilder";
      writer.write("");
      writer.write(
          "/// <summary>Maps the service's operations over <c>protocols</c>, by default its first"
              + " protocol.</summary>");
      writer.write(EXTENSION);
      writer.write(
          "static member Map$L(endpoints: $L, ?protocols: $L) : $L =",
          contract,
          routes,
          protocolEnum,
          routes);
      writer.indent();
      writer.write("global.System.ArgumentNullException.ThrowIfNull(endpoints)");
      writer.write(
          "let protocols = global.Microsoft.FSharp.Core.Operators.defaultArg protocols $L.$L",
          protocolEnum,
          protocols.get(0).name());
      writer.write("if (protocols &&& ~~~$1L.All) <> $1L.None then", protocolEnum).indent();
      writer.write(
          "raise (global.System.ArgumentOutOfRangeException(\"protocols\", box protocols, \"Unknown"
              + " $L value.\"))",
          protocolEnum);
      writer.dedent();
      writer.write(
          "let selected = global.System.Collections.Generic.List<$LSmithyServerProtocol>()",
          ASP_NET_CORE);
      for (ProtocolInfo protocol : protocols) {
        writer.write("if (protocols &&& $1L.$2L) <> $1L.None then", protocolEnum, protocol.name());
        writer.indent();
        writer.write(
            "selected.Add($LSmithyServerProtocol($L, global.$L()))",
            ASP_NET_CORE,
            FSharpNaming.stringLiteral(protocol.name()),
            protocol.runtimeTypeName());
        writer.dedent();
      }
      writer.write(
          "$LSmithyAspNetCoreServer.MapSmithyService(endpoints, $LDefinition.Catalog, selected)",
          ASP_NET_CORE,
          contract);
      writer.dedent();
    }
    writer.dedent().write("");
  }

  private boolean isStreaming(OperationShape operation) {
    return ShapeSupport.isStreamingShape(model, operation.getInputShape())
        || ShapeSupport.isStreamingShape(model, operation.getOutputShape());
  }

  private static String handlerName(OperationShape operation) {
    return "I" + FSharpNaming.typeName(operation.getId().getName()) + "Handler";
  }

  private static String methodName(OperationShape operation) {
    return FSharpNaming.typeName(operation.getId().getName()) + "Async";
  }
}

/*
 * Server-side code generator. Emits:
 *   - one `I{Operation}Handler` per operation (streaming surface derived from the model)
 *   - aggregate `I{Service}ServiceHandler`
 *   - a DI-registered service definition that binds each independently registered operation
 *     handler into a protocol-neutral executable catalog
 *   - `{Service}ServiceServerExtensions` with AddXxxHandler<THandler>(IServiceCollection)
 *   - `{Service}ServiceProtocols` flags and `Map{Service}Service(..., protocols)` that bind
 *     selected protocol routes to the shared handler
 *
 * Endpoints are thin: each maps a route to a handler method and the operation's bound protocol and
 * delegates to SmithyAspNetCoreHost, which runs the shared SmithyServerRuntime dispatch. No
 * per-operation deserialize/invoke/catch/serialize/write is generated.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.codegen.core.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpProtocol;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.node.ObjectNode;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.RequiredTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ServerGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public ServerGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> ops =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());
    ops.forEach(op -> writer.reserveName(operationJsonSchemasClass(op)));

    List<CSharpProtocol> serverProtocols = ProtocolSupport.serverProtocols(context, service);
    List<PromptDefinition> prompts = promptDefinitions(ops);
    boolean emitsAspNetCore = !serverProtocols.isEmpty();

    writer.addImport(RuntimeTypes.MS_EXT_DI);
    writer.addImport(RuntimeTypes.MS_EXT_DI_EXTENSIONS);
    if (emitsAspNetCore) {
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
      writer.addImport(RuntimeTypes.NSMITHY_SERVER_ASPNETCORE);
    }

    String serviceTypeName = CSharpNaming.typeName(service.getId().getName());
    String contract = serviceContractName(serviceTypeName);
    String aggInterface = "I" + contract + "Handler";

    // Per-operation handler interfaces (streaming surface derived from the model).
    for (OperationShape op : ops) {
      writeOperationHandler(sp, op);
    }

    String inherits =
        ops.isEmpty()
            ? ""
            : " : " + ops.stream().map(this::opHandlerName).collect(Collectors.joining(", "));
    writer.writeXmlDocs(service);
    writer.write("public interface $L$L { }", aggInterface, inherits);
    writer.write("");

    if (emitsAspNetCore) {
      writeProtocolEnum(contract, serverProtocols);
      writer.write("");
    }

    writeServiceDefinition(ops, prompts, contract);
    writer.write("");
    writeServerExtensions(ops, contract, aggInterface, serverProtocols);
  }

  private void writeOperationHandler(SymbolProvider sp, OperationShape op) {
    writer.writeXmlDocs(op, operationParameterDocs(op));
    writer.pushState();
    try {
      writer.putContext("handler", opHandlerName(op));
      writer.putContext(
          "operation",
          writer.consumer(
              w -> {
                w.writeXmlDocs(op, operationParameterDocs(op));
                w.write("$L;", serverOperationSignature(sp, op));
              }));
      writer.write(
          """
          public interface ${handler:L}
          {
              ${operation:C|}
          }
          """);
    } finally {
      writer.popState();
    }
    writer.write("");
  }

  // ---------------- DI registration ----------------

  private void writeServiceDefinition(
      List<OperationShape> ops, List<PromptDefinition> prompts, String contract) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("serviceDefinition", RuntimeTypes.I_SERVICE_DEFINITION);
      writer.putContext("serviceSchema", RuntimeTypes.SERVICE_SCHEMA);
      writer.putContext("schema", SchemaGenerator.serviceSchemaAccessor(writer, context, service));
      writer.putContext("catalog", RuntimeTypes.SERVICE_OPERATION_CATALOG);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("prompts", writer.consumer(w -> writePromptDefinitions(prompts)));
      writer.putContext("operations", writer.consumer(w -> writeCatalogOperations(ops)));
      writer.putContext(
          "jsonSchemas",
          writer.consumer(
              w -> {
                if (ops.stream().anyMatch(op -> !isStreaming(op))) {
                  w.write("");
                  writeOperationJsonSchemas(ops);
                }
              }));
      writer.write(
          """
          public sealed class ${contract:L}Definition : ${serviceDefinition:T}
          {
              public ${serviceSchema:T} Schema => ${schema:L};

              ${prompts:C|}

              /// <summary>The service's operation table, shared by every host adapter.</summary>
              internal static ${catalog:T} Catalog { get; } = new(
                  ${schema:L},
                  ${operations:C|}
              );

              public ${catalog:T} Operations => Catalog;
              ${jsonSchemas:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writePromptDefinitions(List<PromptDefinition> prompts) {
    writer.pushState();
    try {
      writer.putContext("promptDefinition", RuntimeTypes.SERVICE_PROMPT_DEFINITION);
      writer.putContext(
          "prompts", writer.consumer(w -> prompts.forEach(this::writePromptDefinition)));
      writer.write(
          """
          public IReadOnlyList<${promptDefinition:T}> Prompts { get; } =
          [
              ${prompts:C|}
          ];
          """);
    } finally {
      writer.popState();
    }
  }

  private void writePromptDefinition(PromptDefinition prompt) {
    writer.pushState();
    try {
      writer.putContext("promptDefinition", RuntimeTypes.SERVICE_PROMPT_DEFINITION);
      writer.putContext("name", CSharpNaming.formatString(prompt.name()));
      writer.putContext("description", CSharpNaming.formatString(prompt.description()));
      writer.putContext("template", CSharpNaming.formatString(prompt.template()));
      writer.putContext(
          "preferWhen",
          prompt.preferWhen() == null ? "null" : CSharpNaming.formatString(prompt.preferWhen()));
      writer.putContext(
          "arguments",
          writer.consumer(
              w -> {
                for (PromptArgumentDefinition argument : prompt.arguments()) {
                  w.write(
                      "new $T($L, $L, $L),",
                      RuntimeTypes.SERVICE_PROMPT_ARGUMENT_DEFINITION,
                      CSharpNaming.formatString(argument.name()),
                      argument.description() == null
                          ? "null"
                          : CSharpNaming.formatString(argument.description()),
                      argument.required() ? "true" : "false");
                }
              }));
      writer.write(
          """
          new ${promptDefinition:T}(
              ${name:L},
              ${description:L},
              ${template:L},
              ${preferWhen:L},
              [
                  ${arguments:C|}
              ]
          ),
          """);
    } finally {
      writer.popState();
    }
  }

  /**
   * The operation table entries: each operation's schema, its handler resolved from a request's
   * services, and, for unary operations, its JSON Schemas.
   */
  private void writeCatalogOperations(List<OperationShape> ops) {
    for (int i = 0; i < ops.size(); i++) {
      OperationShape op = ops.get(i);
      String resolver =
          "services => "
              + unaryAdapter(
                  op, writer.format("services.GetRequiredService<$L>()", opHandlerName(op)));
      String separator = i + 1 == ops.size() ? "" : ",";
      if (isStreaming(op)) {
        writer.write(
            "$T.Create($L, $L)$L",
            RuntimeTypes.SERVER_OPERATION,
            SchemaGenerator.operationSchemaAccessor(writer, context, op),
            resolver,
            separator);
      } else {
        writer.write(
            "$T.Create($L, $L, $L.Value)$L",
            RuntimeTypes.SERVER_OPERATION,
            SchemaGenerator.operationSchemaAccessor(writer, context, op),
            resolver,
            operationJsonSchemasClass(op),
            separator);
      }
    }
  }

  private void writeServerExtensions(
      List<OperationShape> ops,
      String contract,
      String aggInterface,
      List<CSharpProtocol> serverProtocols) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("handler", aggInterface);
      writer.putContext("serviceCollection", RuntimeTypes.I_SERVICE_COLLECTION);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("serviceDescriptor", RuntimeTypes.SERVICE_DESCRIPTOR);
      writer.putContext("serviceDefinition", RuntimeTypes.I_SERVICE_DEFINITION);
      writer.putContext(
          "registrations",
          writer.consumer(
              w -> {
                if (!serverProtocols.isEmpty()) {
                  w.write("services.AddSmithyServer();");
                }
                w.write(
                    """
                    services.Add${contract:L}();
                    services.AddSingleton<THandler>();
                    services.AddSingleton<${handler:L}>(serviceProvider =>
                        serviceProvider.GetRequiredService<THandler>());\
                    """);
                for (OperationShape op : ops) {
                  w.write(
                      "services.AddSingleton<$L>(serviceProvider =>"
                          + " serviceProvider.GetRequiredService<THandler>());",
                      opHandlerName(op));
                }
              }));
      writer.putContext(
          "endpointMapping",
          writer.consumer(
              w -> {
                if (!serverProtocols.isEmpty()) {
                  w.write("");
                  writeEndpointMapping(contract, serverProtocols);
                }
              }));
      writer.write(
          """
          public static class ${contract:L}ServerExtensions
          {
              public static ${serviceCollection:T} Add${contract:L}(this ${serviceCollection:T} services)
              {
                  ${argumentNullException:T}.ThrowIfNull(services);
                  services.TryAddEnumerable(
                      ${serviceDescriptor:T}.Singleton<${serviceDefinition:T}, ${contract:L}Definition>());
                  return services;
              }

              public static ${serviceCollection:T} Add${contract:L}Handler<THandler>(
                  this ${serviceCollection:T} services)
                  where THandler : class, ${handler:L}
              {
                  ${argumentNullException:T}.ThrowIfNull(services);

                  ${registrations:C|}
                  return services;
              }
              ${endpointMapping:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeEndpointMapping(String contract, List<CSharpProtocol> serverProtocols) {
    writeSelectableMapMethod(contract, serverProtocols, protocolEnumName(contract));
  }

  private void writeOperationJsonSchemas(List<OperationShape> ops) {
    for (OperationShape op : ops) {
      if (isStreaming(op)) {
        continue;
      }
      writer.pushState();
      try {
        writer.putContext("schemaClass", operationJsonSchemasClass(op));
        writer.putContext("operationJsonSchemas", RuntimeTypes.OPERATION_JSON_SCHEMAS);
        writer.putContext(
            "inputSchema",
            CSharpNaming.formatString(
                JsonSchemaGenerator.generate(context.model(), op.getInputShape())));
        writer.putContext(
            "outputSchema",
            CSharpNaming.formatString(
                JsonSchemaGenerator.generate(context.model(), op.getOutputShape())));
        op.getTrait(DocumentationTrait.class)
            .ifPresent(
                trait ->
                    writer.putContext("description", CSharpNaming.formatString(trait.getValue())));
        writer.write(
            """
            private static class ${schemaClass:L}
            {
                public static ${operationJsonSchemas:T} Value { get; } = new(
                    ${inputSchema:L},
                    ${outputSchema:L}${?description},
                    ${description:L}${/description}
                );
            }
            """);
        writer.write("");
      } finally {
        writer.popState();
      }
    }
  }

  private boolean isStreaming(OperationShape op) {
    return ShapeSupport.isStreamingShape(context.model(), op.getInputShape())
        || ShapeSupport.isStreamingShape(context.model(), op.getOutputShape());
  }

  private static String operationJsonSchemasClass(OperationShape op) {
    return CSharpNaming.typeName(op.getId().getName()) + "JsonSchemas";
  }

  // ---------------- endpoint mapping ----------------

  private void writeProtocolEnum(String contract, List<CSharpProtocol> serverProtocols) {
    writer.pushState();
    try {
      writer.putContext("flags", writer.attributeName(RuntimeTypes.FLAGS_ATTRIBUTE));
      writer.putContext("enumName", protocolEnumName(contract));
      writer.putContext(
          "allProtocols",
          serverProtocols.stream().map(CSharpProtocol::name).collect(Collectors.joining(" | ")));
      writer.putContext(
          "protocols",
          writer.consumer(
              w -> {
                for (int i = 0; i < serverProtocols.size(); i++) {
                  w.write("$L = $L,", serverProtocols.get(i).name(), 1 << i);
                }
              }));
      writer.write(
          """
          [${flags:L}]
          public enum ${enumName:L}
          {
              None = 0,
              ${protocols:C|}
              All = ${allProtocols:L},
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeSelectableMapMethod(
      String contract, List<CSharpProtocol> serverProtocols, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("protocolEnum", protocolEnum);
      writer.putContext("defaultProtocol", serverProtocols.get(0).name());
      writer.putContext("endpointRouteBuilder", RuntimeTypes.I_ENDPOINT_ROUTE_BUILDER);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext(
          "argumentOutOfRangeException", RuntimeTypes.ARGUMENT_OUT_OF_RANGE_EXCEPTION);
      writer.putContext("list", RuntimeTypes.LIST);
      writer.putContext("serverProtocol", RuntimeTypes.SMITHY_SERVER_PROTOCOL);
      writer.putContext(
          "protocolMappings",
          writer.consumer(w -> writeSelectedProtocolMappings(serverProtocols, protocolEnum)));
      writer.write(
          """
          public static ${endpointRouteBuilder:T} Map${contract:L}(
              this ${endpointRouteBuilder:T} endpoints,
              ${protocolEnum:L} protocols = ${protocolEnum:L}.${defaultProtocol:L})
          {
              ${argumentNullException:T}.ThrowIfNull(endpoints);
              if ((protocols & ~${protocolEnum:L}.All) != 0)
              {
                  throw new ${argumentOutOfRangeException:T}(
                      nameof(protocols), protocols, "Unknown ${protocolEnum:L} value.");
              }

              var selected = new ${list:T}<${serverProtocol:T}>();
              ${protocolMappings:C|}

              return endpoints.MapSmithyService(${contract:L}Definition.Catalog, selected);
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeSelectedProtocolMappings(
      List<CSharpProtocol> serverProtocols, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("protocolEnum", protocolEnum);
      for (CSharpProtocol protocol : serverProtocols) {
        writer.putContext("protocol", protocol.name());
        writer.putContext("protocolType", protocol.runtimeType());
        writer.write(
            """
            if ((protocols & ${protocolEnum:L}.${protocol:L}) != 0)
            {
                selected.Add(new("${protocol:L}", new ${protocolType:T}()));
            }
            """);
      }
    } finally {
      writer.popState();
    }
  }

  // ---------------- handler adapters ----------------

  // Use a method group when the handler's arity and return type match the runtime delegate.
  // Adapt unit input (no input parameter) and unit output (Task instead of Task<SmithyUnit>)
  // with a lambda.

  private String unaryAdapter(OperationShape op, String handlerVariable) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String method = handlerMethod(op, handlerVariable);
    if (hasInput && hasOutput) {
      return method;
    }

    String call = hasInput ? method + "(input, ct)" : method + "(ct)";
    String param = hasInput ? "input" : "_";
    return hasOutput
        ? writer.format("($L, ct) => $L", param, call)
        : writer.format(
            "async ($L, ct) => { await $L.ConfigureAwait(false); return $T.Value; }",
            param,
            call,
            RuntimeTypes.SMITHY_UNIT);
  }

  private String handlerMethod(OperationShape op, String handlerVariable) {
    return handlerVariable + "." + CSharpNaming.typeName(op.getId().getName()) + "Async";
  }

  // ---------------- prompts ----------------

  private List<PromptDefinition> promptDefinitions(List<OperationShape> ops) {
    var prompts = new java.util.ArrayList<PromptDefinition>();
    addPromptDefinitions(service, prompts);
    for (OperationShape op : ops) {
      addPromptDefinitions(op, prompts);
    }
    return List.copyOf(prompts);
  }

  private void addPromptDefinitions(Shape owner, List<PromptDefinition> prompts) {
    owner
        .findTrait(TraitIds.PROMPTS)
        .ifPresent(
            trait -> {
              var entries =
                  trait.toNode().expectObjectNode().getStringMap().entrySet().stream()
                      .sorted(Map.Entry.comparingByKey())
                      .toList();
              for (var entry : entries) {
                ObjectNode definition = entry.getValue().expectObjectNode();
                String description = definition.expectStringMember("description").getValue();
                String template = definition.expectStringMember("template").getValue();
                String preferWhen =
                    definition.getStringMember("preferWhen").map(n -> n.getValue()).orElse(null);
                List<PromptArgumentDefinition> arguments =
                    definition
                        .getStringMember("arguments")
                        .map(node -> promptArguments(node.getValue()))
                        .orElse(List.of());
                prompts.add(
                    new PromptDefinition(
                        entry.getKey(), description, template, preferWhen, arguments));
              }
            });
  }

  private List<PromptArgumentDefinition> promptArguments(String shapeId) {
    StructureShape structure =
        context.model().expectShape(ShapeId.from(shapeId), StructureShape.class);
    return structure.getAllMembers().values().stream()
        .sorted(Comparator.comparing(member -> member.getMemberName()))
        .map(
            member ->
                new PromptArgumentDefinition(
                    member.getMemberName(),
                    member
                        .getTrait(DocumentationTrait.class)
                        .map(trait -> trait.getValue())
                        .orElse(null),
                    member.hasTrait(RequiredTrait.class)))
        .toList();
  }

  // ---------------- helpers ----------------

  private static String protocolEnumName(String contract) {
    return contract + "Protocols";
  }

  private static String serviceContractName(String serviceTypeName) {
    return serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
  }

  private String opHandlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  private String serverOperationSignature(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String inputType =
        hasInput ? writer.typeName(sp.toSymbol(model.expectShape(op.getInputShape()))) : null;
    String outputType =
        hasOutput ? writer.typeName(sp.toSymbol(model.expectShape(op.getOutputShape()))) : null;
    String returnType =
        hasOutput
            ? writer.typeName(RuntimeTypes.TASK) + "<" + outputType + ">"
            : writer.typeName(RuntimeTypes.TASK);
    String params = hasInput ? inputType + " input, " : "";
    return writer.format(
        "$L $L($L$T cancellationToken = default)",
        returnType,
        name,
        params,
        RuntimeTypes.CANCELLATION_TOKEN);
  }

  private Map<String, String> operationParameterDocs(OperationShape op) {
    Model model = context.model();
    if (ShapeSupport.isUnit(op.getInputShape())) {
      return Map.of();
    }
    Map<String, String> docs = new LinkedHashMap<>();
    model
        .expectShape(op.getInputShape())
        .getTrait(DocumentationTrait.class)
        .ifPresent(trait -> docs.put("input", trait.getValue()));
    return docs;
  }

  private record PromptDefinition(
      String name,
      String description,
      String template,
      String preferWhen,
      List<PromptArgumentDefinition> arguments) {}

  private record PromptArgumentDefinition(String name, String description, boolean required) {}
}

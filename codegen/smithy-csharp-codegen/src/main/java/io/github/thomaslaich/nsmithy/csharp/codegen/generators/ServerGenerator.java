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

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
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
import software.amazon.smithy.model.traits.HttpTrait;
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

    List<Kind> serverKinds = serverKinds();
    List<PromptDefinition> prompts = promptDefinitions(ops);
    if (serverKinds.contains(Kind.GRPC)) {
      ops.forEach(op -> ShapeSupport.requireGrpcEventStreamWrapperIsFlattenable(model, op));
    }
    boolean emitsAspNetCore = !serverKinds.isEmpty();

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
      writeProtocolEnum(contract, serverKinds);
      writer.write("");
    }

    writeServiceDefinition(ops, prompts, contract);
    writer.write("");
    writeServerExtensions(ops, contract, aggInterface, serverKinds);
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

  /** Protocols that emit an ASP.NET Core server, in declared precedence order. */
  private List<Kind> serverKinds() {
    return ProtocolSupport.declaredKinds(service).stream()
        .filter(
            kind ->
                kind == Kind.RPC_V2_CBOR
                    || kind == Kind.SIMPLE_REST_JSON
                    || kind == Kind.REST_JSON_1
                    || kind == Kind.GRPC)
        .collect(Collectors.toList());
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
      writer.putContext(
          "handlers",
          writer.consumer(
              w -> {
                for (int i = 0; i < ops.size(); i++) {
                  w.write(
                      "services.GetRequiredService<$L>()$L",
                      opHandlerName(ops.get(i)),
                      i + 1 == ops.size() ? "" : ",");
                }
              }));
      writer.putContext(
          "catalogMembers",
          writer.consumer(
              w -> {
                writeOperationCatalogFactory(ops);
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

              public ${catalog:T} CreateOperationCatalog(IServiceProvider services)
              {
                  ${argumentNullException:T}.ThrowIfNull(services);
                  return CreateOperationCatalog(
                      ${handlers:C|}
                  );
              }

              ${catalogMembers:C|}
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

  private void writeOperationCatalogFactory(List<OperationShape> ops) {
    String parameters =
        ops.stream()
            .map(op -> opHandlerName(op) + " " + operationHandlerVariable(op))
            .collect(Collectors.joining(", "));
    writer.pushState();
    try {
      writer.putContext("parameters", parameters);
      writer.putContext("catalog", RuntimeTypes.SERVICE_OPERATION_CATALOG);
      writer.putContext("schema", SchemaGenerator.serviceSchemaAccessor(writer, context, service));
      writer.putContext("operations", writer.consumer(w -> writeCatalogOperations(ops)));
      writer.write(
          """
          private static ${catalog:T} CreateOperationCatalog(${parameters:L})
          {
              return new ${catalog:T}(
                  ${schema:L},
                  [
                      ${operations:C|}
                  ]
              );
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeCatalogOperations(List<OperationShape> ops) {
    for (OperationShape op : ops) {
      if (isStreaming(op)) {
        writer.write(
            "$T.Create($L, $L),",
            RuntimeTypes.SERVICE_OPERATION,
            SchemaGenerator.operationSchemaAccessor(writer, context, op),
            unaryAdapter(op, operationHandlerVariable(op)));
      } else {
        writer.write(
            "$T.Create($L, $L, $L.Value),",
            RuntimeTypes.SERVICE_OPERATION,
            SchemaGenerator.operationSchemaAccessor(writer, context, op),
            unaryAdapter(op, operationHandlerVariable(op)),
            operationJsonSchemasClass(op));
      }
    }
  }

  private void writeServerExtensions(
      List<OperationShape> ops, String contract, String aggInterface, List<Kind> serverKinds) {
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
                if (!serverKinds.isEmpty()) {
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
                if (!serverKinds.isEmpty()) {
                  w.write("");
                  writeEndpointMapping(ops, contract, serverKinds);
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

  private void writeEndpointMapping(
      List<OperationShape> ops, String contract, List<Kind> serverKinds) {
    String protocolEnum = protocolEnumName(contract);
    writeProtocolFields(ops, serverKinds);
    writer.write("");
    writeSelectableMapMethod(contract, serverKinds, protocolEnum);
    writer.write("");
    writeRouteConflictHelper(contract, protocolEnum);
    for (Kind kind : serverKinds) {
      writer.write("");
      writeProtocolMapHelper(kind, ops, contract, protocolEnum);
    }
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
        writer.write(
            """
            private static class ${schemaClass:L}
            {
                public static ${operationJsonSchemas:T} Value { get; } = new(
                    ${inputSchema:L},
                    ${outputSchema:L}
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

  private void writeProtocolEnum(String contract, List<Kind> serverKinds) {
    writer.pushState();
    try {
      writer.putContext("flags", writer.attributeName(RuntimeTypes.FLAGS_ATTRIBUTE));
      writer.putContext("enumName", protocolEnumName(contract));
      writer.putContext(
          "allProtocols",
          serverKinds.stream().map(ServerGenerator::mapSuffix).collect(Collectors.joining(" | ")));
      writer.putContext(
          "protocols",
          writer.consumer(
              w -> {
                for (int i = 0; i < serverKinds.size(); i++) {
                  w.write("$L = $L,", mapSuffix(serverKinds.get(i)), 1 << i);
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

  private void writeProtocolFields(List<OperationShape> ops, List<Kind> serverKinds) {
    writer.pushState();
    try {
      writer.putContext("serviceProtocol", RuntimeTypes.I_SERVICE_PROTOCOL);
      writer.putContext("operationProtocol", RuntimeTypes.I_SERVER_OPERATION_PROTOCOL);
      writer.putContext(
          "serviceSchema", SchemaGenerator.serviceSchemaAccessor(writer, context, service));
      for (Kind kind : serverKinds) {
        writer.putContext("protocol", mapSuffix(kind));
        writer.putContext("protocolType", ProtocolSupport.protocolType(kind));
        writer.write(
            "private static readonly ${serviceProtocol:T} ${protocol:L}ServiceProtocol = new"
                + " ${protocolType:T}().ForService(${serviceSchema:L});");
        for (OperationShape op : ops) {
          if (!canBindOperation(kind, op)) {
            continue;
          }
          writer.putContext(
              "inputType", SchemaGenerator.operationShapeType(writer, context, op.getInputShape()));
          writer.putContext(
              "outputType",
              SchemaGenerator.operationShapeType(writer, context, op.getOutputShape()));
          writer.putContext("field", operationProtocolField(kind, op));
          writer.putContext(
              "operationSchema", SchemaGenerator.operationSchemaAccessor(writer, context, op));
          writer.write(
              "private static readonly ${operationProtocol:T}<${inputType:L}, ${outputType:L}>"
                  + " ${field:L} ="
                  + " ${protocol:L}ServiceProtocol.ForServerOperation(${operationSchema:L});");
        }
      }
    } finally {
      writer.popState();
    }
  }

  private void writeSelectableMapMethod(
      String contract, List<Kind> serverKinds, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("protocolEnum", protocolEnum);
      writer.putContext("defaultProtocol", mapSuffix(serverKinds.get(0)));
      writer.putContext("endpointRouteBuilder", RuntimeTypes.I_ENDPOINT_ROUTE_BUILDER);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext(
          "argumentOutOfRangeException", RuntimeTypes.ARGUMENT_OUT_OF_RANGE_EXCEPTION);
      writer.putContext("hashSet", RuntimeTypes.HASH_SET);
      writer.putContext("stringComparer", RuntimeTypes.STRING_COMPARER);
      writer.putContext(
          "protocolMappings",
          writer.consumer(w -> writeSelectedProtocolMappings(contract, serverKinds, protocolEnum)));
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

              var mappedRoutes = new ${hashSet:T}<string>(${stringComparer:T}.Ordinal);
              ${protocolMappings:C|}

              return endpoints;
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeSelectedProtocolMappings(
      String contract, List<Kind> serverKinds, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("protocolEnum", protocolEnum);
      for (Kind kind : serverKinds) {
        writer.putContext("protocol", mapSuffix(kind));
        writer.write(
            """
            if ((protocols & ${protocolEnum:L}.${protocol:L}) != 0)
            {
                Map${contract:L}${protocol:L}(endpoints, mappedRoutes);
            }
            """);
      }
    } finally {
      writer.popState();
    }
  }

  private void writeRouteConflictHelper(String contract, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("protocolEnum", protocolEnum);
      writer.putContext("hashSet", RuntimeTypes.HASH_SET);
      writer.putContext("invalidOperationException", RuntimeTypes.INVALID_OPERATION_EXCEPTION);
      writer.write(
          """
          private static void EnsureRouteAvailable(
              ${hashSet:T}<string> mappedRoutes,
              string method,
              string routePattern,
              ${protocolEnum:L} protocol)
          {
              var route = method + " " + routePattern;
              if (!mappedRoutes.Add(route))
              {
                  throw new ${invalidOperationException:T}(
                      "Mapping " + protocol + " for ${contract:L} would register duplicate route '" + route
                      + "'. Map conflicting protocols on different endpoint route builders, hosts, or ports.");
              }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeProtocolMapHelper(
      Kind kind, List<OperationShape> ops, String contract, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("contract", contract);
      writer.putContext("protocol", mapSuffix(kind));
      writer.putContext("endpointRouteBuilder", RuntimeTypes.I_ENDPOINT_ROUTE_BUILDER);
      writer.putContext("hashSet", RuntimeTypes.HASH_SET);
      writer.putContext(
          "operations",
          writer.consumer(
              w -> {
                for (OperationShape op : ops) {
                  if (canBindOperation(kind, op)) {
                    writeOperationMap(kind, op, protocolEnum);
                    w.write("");
                  }
                }
              }));
      writer.write(
          """
          private static void Map${contract:L}${protocol:L}(
              ${endpointRouteBuilder:T} endpoints, ${hashSet:T}<string> mappedRoutes)
          {
              ${operations:C|}
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeOperationMap(Kind kind, OperationShape op, String protocolEnum) {
    writer.pushState();
    try {
      writer.putContext("handler", opHandlerName(op));
      writer.putContext("protocolEnum", protocolEnum);
      writer.putContext("protocol", mapSuffix(kind));
      writer.putContext("httpContext", RuntimeTypes.HTTP_CONTEXT);
      writer.putContext("fromServices", writer.attributeName(RuntimeTypes.FROM_SERVICES_ATTRIBUTE));
      writer.putContext("runtime", RuntimeTypes.SMITHY_SERVER_RUNTIME);
      writer.putContext("cancellationToken", RuntimeTypes.CANCELLATION_TOKEN);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      boolean rest = kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1;
      if (rest) {
        HttpTrait http = op.expectTrait(HttpTrait.class);
        writer.putContext("method", CSharpNaming.formatString(http.getMethod()));
        writer.putContext("route", CSharpNaming.formatString(routePattern(http)));
        writer.putContext(
            "dispatch",
            writer.consumer(
                w -> {
                  writeStaticQueryValidation(http);
                  writeDispatch(kind, op);
                }));
        writer.write(
            """
            EnsureRouteAvailable(mappedRoutes, ${method:L}, ${route:L}, ${protocolEnum:L}.${protocol:L});
            endpoints.MapMethods(${route:L}, [${method:L}], async (
                ${httpContext:T} httpContext,
                [${fromServices:L}] ${runtime:T} runtime,
                ${handler:L} handler,
                ${cancellationToken:T} cancellationToken) =>
            {
                ${argumentNullException:T}.ThrowIfNull(httpContext);
                ${argumentNullException:T}.ThrowIfNull(handler);

                ${dispatch:C|}
            });
            """);
        return;
      }

      // rpcv2Cbor and gRPC use structured POST routes derived from the shape ids.
      String uri =
          kind == Kind.GRPC
              ? "/"
                  + service.getId().getNamespace()
                  + "."
                  + service.getId().getName()
                  + "/"
                  + op.getId().getName()
              : "/service/" + service.getId().getName() + "/operation/" + op.getId().getName();
      writer.putContext("route", CSharpNaming.formatString(uri));
      writer.putContext("dispatch", writer.consumer(w -> writeDispatch(kind, op)));
      writer.write(
          """
          EnsureRouteAvailable(mappedRoutes, "POST", ${route:L}, ${protocolEnum:L}.${protocol:L});
          endpoints.MapPost(${route:L}, async (
              ${httpContext:T} httpContext,
              [${fromServices:L}] ${runtime:T} runtime,
              ${handler:L} handler,
              ${cancellationToken:T} cancellationToken) =>
          {
              ${argumentNullException:T}.ThrowIfNull(httpContext);
              ${argumentNullException:T}.ThrowIfNull(handler);

              ${dispatch:C|}
          });
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeDispatch(Kind kind, OperationShape op) {
    Model model = context.model();
    boolean streamRequestBody =
        isInputStreaming(model, op)
            || ((kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1)
                && ShapeSupport.isStreamingBlobShape(model, op.getInputShape()));
    writer.write(
        "await $T.DispatchAsync(runtime, httpContext, $L, $L, $L,"
            + " cancellationToken).ConfigureAwait(false);",
        RuntimeTypes.SMITHY_ASP_NET_CORE_HOST,
        operationProtocolField(kind, op),
        unaryAdapter(op),
        streamRequestBody ? "true" : "false");
  }

  // ---------------- handler adapters ----------------

  // Use a method group when the handler's arity and return type match the runtime delegate.
  // Adapt unit input (no input parameter) and unit output (Task instead of Task<SmithyUnit>)
  // with a lambda.

  private String unaryAdapter(OperationShape op) {
    return unaryAdapter(op, "handler");
  }

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

  // ---------------- routing helpers ----------------

  private String routePattern(HttpTrait http) {
    String uri = http.getUri().toString();
    int queryIndex = uri.indexOf('?');
    String path = queryIndex >= 0 ? uri.substring(0, queryIndex) : uri;
    // Smithy greedy labels `{foo+}` map to ASP.NET Core catch-all route params `{**foo}`.
    return path.replaceAll("\\{(\\w+)\\+\\}", "{**$1}");
  }

  private void writeStaticQueryValidation(HttpTrait http) {
    String uri = http.getUri().toString();
    int queryIndex = uri.indexOf('?');
    if (queryIndex < 0 || queryIndex == uri.length() - 1) {
      return;
    }

    String query = uri.substring(queryIndex + 1);
    for (String segment : query.split("&")) {
      if (segment.isEmpty()) {
        continue;
      }

      int equalsIndex = segment.indexOf('=');
      String name = equalsIndex >= 0 ? segment.substring(0, equalsIndex) : segment;
      String value = equalsIndex >= 0 ? segment.substring(equalsIndex + 1) : null;
      writer.pushState();
      try {
        writer.putContext("host", RuntimeTypes.SMITHY_ASP_NET_CORE_HOST);
        writer.putContext("name", CSharpNaming.formatString(name));
        writer.putContext("value", value == null ? "null" : CSharpNaming.formatString(value));
        writer.write(
            """
            if (!${host:T}.HasExpectedQueryLiteral(httpContext, ${name:L}, ${value:L}))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }\
            """);
      } finally {
        writer.popState();
      }
    }

    writer.write("");
  }

  // ---------------- helpers ----------------

  private static String mapSuffix(Kind kind) {
    return switch (kind) {
      case RPC_V2_CBOR -> "RpcV2Cbor";
      case SIMPLE_REST_JSON -> "SimpleRestJson";
      case REST_JSON_1 -> "RestJson1";
      case GRPC -> "Grpc";
      default -> throw new IllegalStateException("Unsupported server protocol: " + kind);
    };
  }

  private static String protocolEnumName(String contract) {
    return contract + "Protocols";
  }

  private static String operationProtocolField(Kind kind, OperationShape op) {
    return CSharpNaming.typeName(op.getId().getName()) + mapSuffix(kind) + "Protocol";
  }

  private static String serviceContractName(String serviceTypeName) {
    return serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
  }

  private boolean canBindOperation(Kind kind, OperationShape op) {
    return !isEventStreamOperation(context.model(), op)
        || ProtocolSupport.supportsEventStreams(kind);
  }

  private String opHandlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  private String operationHandlerVariable(OperationShape op) {
    return CSharpNaming.parameterName(op.getId().getName() + "Handler");
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

  private boolean isEventStreamOperation(Model model, OperationShape op) {
    return isInputStreaming(model, op) || isOutputStreaming(model, op);
  }

  private boolean isInputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getInputShape());
  }

  private boolean isOutputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getOutputShape());
  }

  private record PromptDefinition(
      String name,
      String description,
      String template,
      String preferWhen,
      List<PromptArgumentDefinition> arguments) {}

  private record PromptArgumentDefinition(String name, String description, boolean required) {}
}

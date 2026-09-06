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
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
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

  @Override
  public void run() {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> ops =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());

    List<Kind> serverKinds = serverKinds();
    List<PromptDefinition> prompts = promptDefinitions(ops);
    if (serverKinds.contains(Kind.GRPC)) {
      ops.forEach(op -> ShapeSupport.requireGrpcEventStreamWrapperIsFlattenable(model, op));
    }
    boolean emitsAspNetCore = !serverKinds.isEmpty();

    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_SERVER);
    writer.addImport(RuntimeTypes.MS_EXT_DI);
    writer.addImport(RuntimeTypes.MS_EXT_DI_EXTENSIONS);
    if (emitsAspNetCore) {
      writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
      writer.addImport(RuntimeTypes.NSMITHY_HTTP);
      writer.addImport(RuntimeTypes.NSMITHY_SERVER_ASPNETCORE);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_HTTP);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_ROUTING);
      for (Kind kind : serverKinds) {
        writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kind));
      }
    }

    String serviceTypeName = CSharpNaming.typeName(service.getId().getName());
    String contract = serviceContractName(serviceTypeName);
    String aggInterface = "I" + contract + "Handler";

    // Per-operation handler interfaces (streaming surface derived from the model).
    for (OperationShape op : ops) {
      writer.writeXmlDocs(op, operationParameterDocs(op));
      writer.write("public interface $L", opHandlerName(op));
      writer.openBlock(
          "{",
          "}",
          () -> {
            writer.writeXmlDocs(op, operationParameterDocs(op));
            writer.write("$L;", serverOperationSignature(sp, op));
          });
      writer.write("");
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

  // ---------------- DI registration ----------------

  private void writeServiceDefinition(
      List<OperationShape> ops, List<PromptDefinition> prompts, String contract) {
    writer.write("public sealed class $LDefinition : IServiceDefinition", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public ServiceSchema Schema => $L;",
              SchemaGenerator.serviceSchemaAccessor(context, service));
          writer.write("");
          writePromptDefinitions(prompts);
          writer.write("");
          writer.write(
              "public ServiceOperationCatalog CreateOperationCatalog(IServiceProvider services)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(services);");
                writer.write("return CreateOperationCatalog(");
                writer.indent();
                for (int i = 0; i < ops.size(); i++) {
                  OperationShape op = ops.get(i);
                  writer.write(
                      "services.GetRequiredService<$L>()$L",
                      opHandlerName(op),
                      i + 1 == ops.size() ? "" : ",");
                }
                writer.dedent();
                writer.write(");");
              });
          writer.write("");
          writeOperationCatalogFactory(ops);

          if (ops.stream().anyMatch(op -> !isStreaming(op))) {
            writer.write("");
            writeOperationJsonSchemas(ops);
          }
        });
  }

  private void writePromptDefinitions(List<PromptDefinition> prompts) {
    writer.write("public IReadOnlyList<ServicePromptDefinition> Prompts { get; } =");
    writer.write("[");
    writer.indent();
    for (PromptDefinition prompt : prompts) {
      writer.write("new ServicePromptDefinition(");
      writer.indent();
      writer.write("$L,", CSharpNaming.formatString(prompt.name()));
      writer.write("$L,", CSharpNaming.formatString(prompt.description()));
      writer.write("$L,", CSharpNaming.formatString(prompt.template()));
      writer.write(
          "$L,",
          prompt.preferWhen() == null ? "null" : CSharpNaming.formatString(prompt.preferWhen()));
      writer.write("[");
      writer.indent();
      for (PromptArgumentDefinition argument : prompt.arguments()) {
        writer.write(
            "new ServicePromptArgumentDefinition($L, $L, $L),",
            CSharpNaming.formatString(argument.name()),
            argument.description() == null
                ? "null"
                : CSharpNaming.formatString(argument.description()),
            argument.required() ? "true" : "false");
      }
      writer.dedent();
      writer.write("]");
      writer.dedent();
      writer.write("),");
    }
    writer.dedent();
    writer.write("];");
  }

  private void writeOperationCatalogFactory(List<OperationShape> ops) {
    String parameters =
        ops.stream()
            .map(op -> opHandlerName(op) + " " + operationHandlerVariable(op))
            .collect(Collectors.joining(", "));
    writer.write("private static ServiceOperationCatalog CreateOperationCatalog($L)", parameters);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("return new ServiceOperationCatalog(");
          writer.indent();
          writer.write("$L,", SchemaGenerator.serviceSchemaAccessor(context, service));
          writer.write("[");
          writer.indent();
          for (OperationShape op : ops) {
            if (isStreaming(op)) {
              writer.write(
                  "ServiceOperation.Create($L, $L),",
                  SchemaGenerator.operationSchemaAccessor(context, op),
                  unaryAdapter(op, operationHandlerVariable(op)));
            } else {
              writer.write(
                  "ServiceOperation.Create($L, $L, $L.Value),",
                  SchemaGenerator.operationSchemaAccessor(context, op),
                  unaryAdapter(op, operationHandlerVariable(op)),
                  operationJsonSchemasClass(op));
            }
          }
          writer.dedent();
          writer.write("]");
          writer.dedent();
          writer.write(");");
        });
  }

  private void writeServerExtensions(
      List<OperationShape> ops, String contract, String aggInterface, List<Kind> serverKinds) {
    String protocolEnum = protocolEnumName(contract);
    writer.write("public static class $LServerExtensions", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public static IServiceCollection Add$L(this IServiceCollection services)", contract);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(services);");
                writer.write(
                    "services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceDefinition,"
                        + " $LDefinition>());",
                    contract);
                writer.write("return services;");
              });

          writer.write("");
          writer.write(
              "public static IServiceCollection Add$LHandler<THandler>(this IServiceCollection"
                  + " services)",
              contract);
          writer.write("    where THandler : class, $L", aggInterface);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(services);");
                writer.write("");
                if (!serverKinds.isEmpty()) {
                  writer.write("services.AddSmithyServer();");
                }
                writer.write("services.Add$L();", contract);
                writer.write("services.AddSingleton<THandler>();");
                writer.write(
                    "services.AddSingleton<$L>(serviceProvider =>"
                        + " serviceProvider.GetRequiredService<THandler>());",
                    aggInterface);
                for (OperationShape op : ops) {
                  writer.write(
                      "services.AddSingleton<$L>(serviceProvider =>"
                          + " serviceProvider.GetRequiredService<THandler>());",
                      opHandlerName(op));
                }
                writer.write("return services;");
              });

          if (serverKinds.isEmpty()) {
            return;
          }

          writer.write("");
          writeProtocolFields(ops, serverKinds);
          writer.write("");
          writeSelectableMapMethod(contract, serverKinds, protocolEnum);
          writer.write("");
          writeRouteConflictHelper(contract, protocolEnum);

          for (Kind kind : serverKinds) {
            writer.write("");
            writeProtocolMapHelper(kind, ops, contract, protocolEnum);
          }
        });
  }

  private void writeOperationJsonSchemas(List<OperationShape> ops) {
    for (OperationShape op : ops) {
      if (isStreaming(op)) {
        continue;
      }

      writer.write("private static class $L", operationJsonSchemasClass(op));
      writer.openBlock(
          "{",
          "}",
          () -> {
            writer.write("public static OperationJsonSchemas Value { get; } = new(");
            writer.indent();
            writer.write(
                "$L,",
                CSharpNaming.formatString(
                    JsonSchemaGenerator.generate(context.model(), op.getInputShape())));
            writer.write(
                "$L",
                CSharpNaming.formatString(
                    JsonSchemaGenerator.generate(context.model(), op.getOutputShape())));
            writer.dedent();
            writer.write(");");
          });
      writer.write("");
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
    String enumName = protocolEnumName(contract);
    writer.write("[System.Flags]");
    writer.write("public enum $L", enumName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("None = 0,");
          for (int i = 0; i < serverKinds.size(); i++) {
            Kind kind = serverKinds.get(i);
            writer.write("$L = $L,", mapSuffix(kind), 1 << i);
          }
          writer.write(
              "All = $L,",
              serverKinds.stream()
                  .map(ServerGenerator::mapSuffix)
                  .collect(Collectors.joining(" | ")));
        });
  }

  private void writeProtocolFields(List<OperationShape> ops, List<Kind> serverKinds) {
    for (Kind kind : serverKinds) {
      writer.write(
          "private static readonly IServiceProtocol $LServiceProtocol = new $L().ForService($L);",
          mapSuffix(kind),
          ProtocolSupport.protocolType(kind),
          SchemaGenerator.serviceSchemaAccessor(context, service));
      for (OperationShape op : ops) {
        if (!canBindOperation(kind, op)) {
          continue;
        }
        writer.write(
            "private static readonly IServerOperationProtocol<$L, $L> $L ="
                + " $LServiceProtocol.ForServerOperation($L);",
            SchemaGenerator.operationShapeType(context, op.getInputShape()),
            SchemaGenerator.operationShapeType(context, op.getOutputShape()),
            operationProtocolField(kind, op),
            mapSuffix(kind),
            SchemaGenerator.operationSchemaAccessor(context, op));
      }
    }
  }

  private void writeSelectableMapMethod(
      String contract, List<Kind> serverKinds, String protocolEnum) {
    writer.write(
        "public static IEndpointRouteBuilder Map$L(this IEndpointRouteBuilder endpoints,"
            + " $L protocols = $L.$L)",
        contract,
        protocolEnum,
        protocolEnum,
        mapSuffix(serverKinds.get(0)));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
          writer.write("if ((protocols & ~$L.All) != 0)", protocolEnum);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "throw new System.ArgumentOutOfRangeException(nameof(protocols), protocols,"
                          + " \"Unknown $L value.\");",
                      protocolEnum));
          writer.write("");
          writer.write(
              "var mappedRoutes = new System.Collections.Generic.HashSet<string>("
                  + "System.StringComparer.Ordinal);");
          for (Kind kind : serverKinds) {
            writer.write("if ((protocols & $L.$L) != 0)", protocolEnum, mapSuffix(kind));
            writer.openBlock(
                "{",
                "}",
                () -> writer.write("Map$L$L(endpoints, mappedRoutes);", contract, mapSuffix(kind)));
          }
          writer.write("");
          writer.write("return endpoints;");
        });
  }

  private void writeRouteConflictHelper(String contract, String protocolEnum) {
    writer.write(
        "private static void EnsureRouteAvailable(System.Collections.Generic.HashSet<string>"
            + " mappedRoutes, string method, string routePattern, $L protocol)",
        protocolEnum);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("var route = method + \" \" + routePattern;");
          writer.write("if (!mappedRoutes.Add(route))");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write(
                    "throw new System.InvalidOperationException(\"Mapping \" + protocol + \" for"
                        + " $L would register duplicate route '\" + route + \"'. Map conflicting"
                        + " protocols on different endpoint route builders, hosts, or ports.\");",
                    contract);
              });
        });
  }

  private void writeProtocolMapHelper(
      Kind kind, List<OperationShape> ops, String contract, String protocolEnum) {
    writer.write(
        "private static void Map$L$L(IEndpointRouteBuilder endpoints,"
            + " System.Collections.Generic.HashSet<string> mappedRoutes)",
        contract,
        mapSuffix(kind));
    writer.openBlock(
        "{",
        "}",
        () -> {
          for (OperationShape op : ops) {
            if (!canBindOperation(kind, op)) {
              continue;
            }
            writeOperationMap(kind, op, protocolEnum);
            writer.write("");
          }
        });
  }

  private void writeOperationMap(Kind kind, OperationShape op, String protocolEnum) {
    String opInterface = opHandlerName(op);
    boolean rest = kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1;
    if (rest) {
      HttpTrait http = op.expectTrait(HttpTrait.class);
      writer.write(
          "EnsureRouteAvailable(mappedRoutes, $L, $L, $L.$L);",
          CSharpNaming.formatString(http.getMethod()),
          CSharpNaming.formatString(routePattern(http)),
          protocolEnum,
          mapSuffix(kind));
      writer.openBlock(
          "endpoints.MapMethods($L, [$L], async (HttpContext httpContext,"
              + " [Microsoft.AspNetCore.Mvc.FromServices] SmithyServerRuntime runtime, $L handler,"
              + " System.Threading.CancellationToken cancellationToken) => {",
          "});",
          CSharpNaming.formatString(routePattern(http)),
          CSharpNaming.formatString(http.getMethod()),
          opInterface,
          () -> {
            writer.write("System.ArgumentNullException.ThrowIfNull(httpContext);");
            writer.write("System.ArgumentNullException.ThrowIfNull(handler);");
            writer.write("");
            writeStaticQueryValidation(http);
            writeDispatch(kind, op);
          });
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
    writer.write(
        "EnsureRouteAvailable(mappedRoutes, \"POST\", $L, $L.$L);",
        CSharpNaming.formatString(uri),
        protocolEnum,
        mapSuffix(kind));
    writer.openBlock(
        "endpoints.MapPost($L, async (HttpContext httpContext,"
            + " [Microsoft.AspNetCore.Mvc.FromServices] SmithyServerRuntime runtime, $L handler,"
            + " System.Threading.CancellationToken cancellationToken) => {",
        "});",
        CSharpNaming.formatString(uri),
        opInterface,
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(httpContext);");
          writer.write("System.ArgumentNullException.ThrowIfNull(handler);");
          writer.write("");
          writeDispatch(kind, op);
        });
  }

  private void writeDispatch(Kind kind, OperationShape op) {
    Model model = context.model();
    boolean streamRequestBody =
        isInputStreaming(model, op)
            || ((kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1)
                && ShapeSupport.isStreamingBlobShape(model, op.getInputShape()));
    writer.write(
        "await SmithyAspNetCoreHost.DispatchAsync(runtime, httpContext, $L, $L, $L,"
            + " cancellationToken).ConfigureAwait(false);",
        operationProtocolField(kind, op),
        unaryAdapter(op),
        streamRequestBody ? "true" : "false");
  }

  // ---------------- handler adapters ----------------

  // Handler methods return Task<TOutput> / IAsyncEnumerable<TEvent> — the delegate shape the
  // runtime
  // expects — so the adapter is a bare method group whenever arity and return type line up. A
  // lambda
  // is emitted only for the mismatches: a unit input (the handler method takes no input) or a unit
  // output (the handler returns Task, but the runtime expects Task<SmithyUnit>).

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
        ? "(" + param + ", ct) => " + call
        : "async ("
            + param
            + ", ct) => { await "
            + call
            + ".ConfigureAwait(false); return SmithyUnit.Value; }";
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
      writer.write(
          "if (!SmithyAspNetCoreHost.HasExpectedQueryLiteral(httpContext, $L, $L))",
          CSharpNaming.formatString(name),
          value == null ? "null" : CSharpNaming.formatString(value));
      writer.openBlock(
          "{",
          "}",
          () -> {
            writer.write("httpContext.Response.StatusCode = StatusCodes.Status404NotFound;");
            writer.write("return;");
          });
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
        hasInput
            ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())))
            : null;
    String outputType =
        hasOutput
            ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getOutputShape())))
            : null;
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<" + outputType + ">"
            : "System.Threading.Tasks.Task";
    String params = hasInput ? inputType + " input, " : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
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

/*
 * Server-side code generator. Emits:
 *   - one `I{Operation}Handler` per operation (streaming surface derived from the model)
 *   - aggregate `I{Service}ServiceHandler`
 *   - `{Service}ServiceServerExtensions` with AddXxxHandler<THandler>(IServiceCollection)
 *   - one `{Service}Service{Protocol}Extensions` per declared server protocol, each with a
 *     `Map{Service}Service{Protocol}(IEndpointRouteBuilder)` that binds routes to the shared handler
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
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.traits.HttpTrait;
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
    boolean emitsAspNetCore = !serverKinds.isEmpty();

    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.MS_EXT_DI);
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
      writer.write("public interface $L", opHandlerName(op));
      writer.openBlock("{", "}", () -> writer.write("$L;", serverOperationSignature(sp, op)));
      writer.write("");
    }

    String inherits =
        ops.isEmpty()
            ? ""
            : " : " + ops.stream().map(this::opHandlerName).collect(Collectors.joining(", "));
    writer.write("public interface $L$L { }", aggInterface, inherits);
    writer.write("");

    writeServerExtensions(ops, contract, aggInterface);

    for (Kind kind : serverKinds) {
      writer.write("");
      writeProtocolExtensions(sp, kind, ops, contract);
    }
  }

  // ---------------- DI registration ----------------

  private void writeServerExtensions(
      List<OperationShape> ops, String contract, String aggInterface) {
    writer.write("public static class $LServerExtensions", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
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
        });
  }

  // ---------------- per-protocol endpoint extensions ----------------

  private void writeProtocolExtensions(
      SymbolProvider sp, Kind kind, List<OperationShape> ops, String contract) {
    String suffix = mapSuffix(kind);
    writer.write("public static class $L$LExtensions", contract, suffix);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "private static readonly IServiceProtocol ServiceProtocol = new $L().ForService($L);",
              ProtocolSupport.protocolType(kind),
              SchemaGenerator.serviceSchemaAccessor(context, service));
          for (OperationShape op : ops) {
            if (isEventStreamOperation(context.model(), op)) {
              if (kind == Kind.GRPC) {
                writeEventStreamProtocolField(sp, op);
              }
              continue;
            }
            writer.write(
                "private static readonly IOperationProtocol<$L, $L> $LProtocol ="
                    + " ServiceProtocol.ForOperation($L);",
                SchemaGenerator.operationShapeType(context, op.getInputShape()),
                SchemaGenerator.operationShapeType(context, op.getOutputShape()),
                CSharpNaming.typeName(op.getId().getName()),
                SchemaGenerator.operationSchemaAccessor(context, op));
          }
          writer.write("");

          writer.write(
              "public static IEndpointRouteBuilder Map$L$L(this IEndpointRouteBuilder endpoints)",
              contract,
              suffix);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                writer.write("");
                for (OperationShape op : ops) {
                  if (isEventStreamOperation(context.model(), op) && kind != Kind.GRPC) {
                    // Event streams are only served over gRPC today; other protocols skip them.
                    continue;
                  }
                  writeOperationMap(kind, op);
                  writer.write("");
                }
                writer.write("return endpoints;");
              });
        });
  }

  private void writeEventStreamProtocolField(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputType = SchemaGenerator.operationShapeType(context, op.getInputShape());
    String outputType = SchemaGenerator.operationShapeType(context, op.getOutputShape());
    String operationSchema = SchemaGenerator.operationSchemaAccessor(context, op);
    if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
      writer.write(
          "private static readonly IDuplexEventStreamOperationProtocol<$L, $L> $LProtocol ="
              + " ServiceProtocol.ForDuplexEventStreamOperation($L, $L, $L);",
          streamingEventType(sp, model, op.getInputShape()),
          streamingEventType(sp, model, op.getOutputShape()),
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()),
          streamingEventSchema(model, op.getOutputShape()));
    } else if (isOutputStreaming(model, op)) {
      writer.write(
          "private static readonly IOutputEventStreamOperationProtocol<$L, $L> $LProtocol ="
              + " ServiceProtocol.ForOutputEventStreamOperation($L, $L);",
          inputType,
          streamingEventType(sp, model, op.getOutputShape()),
          opName,
          operationSchema,
          streamingEventSchema(model, op.getOutputShape()));
    } else {
      writer.write(
          "private static readonly IInputEventStreamOperationProtocol<$L, $L> $LProtocol ="
              + " ServiceProtocol.ForInputEventStreamOperation($L, $L);",
          streamingEventType(sp, model, op.getInputShape()),
          outputType,
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()));
    }
  }

  private void writeOperationMap(Kind kind, OperationShape op) {
    String opInterface = opHandlerName(op);
    boolean rest = kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1;
    if (rest) {
      HttpTrait http = op.expectTrait(HttpTrait.class);
      writer.openBlock(
          "endpoints.MapMethods($L, [$L], async (HttpContext httpContext, $L handler,"
              + " System.Threading.CancellationToken"
              + " cancellationToken) => {",
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
    writer.openBlock(
        "endpoints.MapPost($L, async (HttpContext httpContext, $L handler,"
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
    String opProtocol = CSharpNaming.typeName(op.getId().getName()) + "Protocol";
    if (isEventStreamOperation(model, op)) {
      if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
        writer.write(
            "await SmithyAspNetCoreHost.DispatchDuplexStreamAsync(httpContext, $L, $L,"
                + " cancellationToken).ConfigureAwait(false);",
            opProtocol,
            duplexAdapter(op));
      } else if (isOutputStreaming(model, op)) {
        writer.write(
            "await SmithyAspNetCoreHost.DispatchOutputStreamAsync(httpContext, $L, $L,"
                + " cancellationToken).ConfigureAwait(false);",
            opProtocol,
            outputStreamAdapter(op));
      } else {
        writer.write(
            "await SmithyAspNetCoreHost.DispatchInputStreamAsync(httpContext, $L, $L,"
                + " cancellationToken).ConfigureAwait(false);",
            opProtocol,
            inputStreamAdapter(op));
      }
      return;
    }

    boolean streamRequestBody =
        (kind == Kind.SIMPLE_REST_JSON || kind == Kind.REST_JSON_1)
            && ShapeSupport.isStreamingBlobShape(model, op.getInputShape());
    writer.write(
        "await SmithyAspNetCoreHost.DispatchAsync(httpContext, $L, $L, $L,"
            + " cancellationToken).ConfigureAwait(false);",
        opProtocol,
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
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String method = handlerMethod(op);
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

  private String outputStreamAdapter(OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    String method = handlerMethod(op);
    return hasInput ? method : "(_, ct) => " + method + "(ct)";
  }

  private String inputStreamAdapter(OperationShape op) {
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String method = handlerMethod(op);
    return hasOutput
        ? method
        : "async (input, ct) => { await "
            + method
            + "(input, ct).ConfigureAwait(false); return SmithyUnit.Value; }";
  }

  private String duplexAdapter(OperationShape op) {
    return handlerMethod(op);
  }

  private String handlerMethod(OperationShape op) {
    return "handler." + CSharpNaming.typeName(op.getId().getName()) + "Async";
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

  private static String serviceContractName(String serviceTypeName) {
    return serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
  }

  private String opHandlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  private String serverOperationSignature(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    boolean inputStreaming = isInputStreaming(model, op);
    boolean outputStreaming = isOutputStreaming(model, op);
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape()) && !inputStreaming;
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape()) && !outputStreaming;
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String inputType =
        inputStreaming
            ? "System.Collections.Generic.IAsyncEnumerable<"
                + streamingEventType(sp, model, op.getInputShape())
                + ">"
            : hasInput
                ? CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(op.getInputShape())))
                : null;
    String outputType =
        outputStreaming
            ? streamingEventType(sp, model, op.getOutputShape())
            : hasOutput
                ? CSharpSymbolProvider.qualified(
                    sp.toSymbol(model.expectShape(op.getOutputShape())))
                : null;
    String returnType =
        outputStreaming
            ? "System.Collections.Generic.IAsyncEnumerable<" + outputType + ">"
            : hasOutput
                ? "System.Threading.Tasks.Task<" + outputType + ">"
                : "System.Threading.Tasks.Task";
    String params = inputStreaming || hasInput ? inputType + " input, " : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
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

  private String streamingEventType(SymbolProvider sp, Model model, ShapeId shapeId) {
    ShapeId target =
        ShapeSupport.streamingMemberTarget(model, shapeId)
            .orElseThrow(() -> new IllegalStateException("Expected streaming shape: " + shapeId));
    return CSharpSymbolProvider.qualified(sp.toSymbol(model.expectShape(target)));
  }

  private String streamingEventSchema(Model model, ShapeId shapeId) {
    ShapeId target =
        ShapeSupport.streamingMemberTarget(model, shapeId)
            .orElseThrow(() -> new IllegalStateException("Expected streaming shape: " + shapeId));
    return SchemaGenerator.shapeSchemaAccessor(context, model.expectShape(target));
  }
}

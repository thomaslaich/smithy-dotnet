/*
 * Server-side code generator. Emits:
 *   - one `I{Operation}Handler` per operation
 *   - aggregate `I{Service}ServiceHandler`
 *   - `{Service}ServiceDescriptor` with per-op SmithyOperationDescriptor + Service
 *   - `{Service}ServiceServerExtensions` with AddXxxHandler<THandler>(IServiceCollection)
 *   - `{Service}ServiceAspNetCoreExtensions` with MapXxxHttp(IEndpointRouteBuilder)
 *
 * Currently scoped to alloy#simpleRestJson services.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.ErrorTrait;
import software.amazon.smithy.model.traits.HttpErrorTrait;
import software.amazon.smithy.model.traits.HttpTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ServerGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final ProtocolSupport.Kind kind;

  public ServerGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.kind = ProtocolSupport.kindOf(s);
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

    boolean emitsAspNet = ProtocolSupport.emitsAspNetCoreServer(service);
    boolean emitsGrpc = ProtocolSupport.isGrpcService(service);

    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.MS_EXT_DI);
    if (emitsAspNet) {
      writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
      writer.addImport(RuntimeTypes.NSMITHY_HTTP);
      writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kind));
      writer.addImport(RuntimeTypes.NSMITHY_SERVER_ASPNETCORE);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_HTTP);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_ROUTING);
    }
    // Native gRPC server: no protoc/Grpc.AspNetCore — the generated map reads/writes the gRPC wire
    // format itself via GrpcProtocol + the ASP.NET helpers, exactly like the rpcv2Cbor server.
    if (emitsGrpc) {
      writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
      writer.addImport(RuntimeTypes.NSMITHY_HTTP);
      writer.addImport(RuntimeTypes.NSMITHY_PROTOCOLS_GRPC);
      writer.addImport(RuntimeTypes.NSMITHY_SERVER_ASPNETCORE);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_HTTP);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_ROUTING);
    }

    String serviceTypeName = CSharpNaming.typeName(service.getId().getName());
    String contract = serviceContractName(serviceTypeName);
    String aggInterface = "I" + contract + "Handler";

    // Per-operation handler interfaces
    for (OperationShape op : ops) {
      writer.write("public interface $L", opHandlerName(op));
      writer.openBlock("{", "}", () -> writer.write("$L;", serverOperationSignature(sp, op)));
      writer.write("");
    }

    // Aggregate interface
    String inherits =
        ops.isEmpty()
            ? ""
            : " : " + ops.stream().map(this::opHandlerName).collect(Collectors.joining(", "));
    writer.write("public interface $L$L { }", aggInterface, inherits);
    writer.write("");

    // ServerExtensions (DI)
    writeServerExtensions(ops, contract, aggInterface);
    writer.write("");

    // ASP.NET Core endpoint extensions (HTTP REST)
    if (emitsAspNet) {
      writeAspNetCoreExtensions(sp, ops, contract);
      writer.write("");
    }

    // Native gRPC endpoint extensions (MapXxxGrpc) — coexists with the REST map for dual-protocol
    // services. Uses GrpcProtocol over HTTP/2; no protoc-generated adapter.
    if (emitsGrpc) {
      writeGrpcAspNetCoreExtensions(sp, ops, contract);
    }
  }

  // ---------------- native gRPC endpoint extensions ----------------

  private void writeGrpcAspNetCoreExtensions(
      SymbolProvider sp, List<OperationShape> ops, String contract) {
    writer.write("public static class $LGrpcExtensions", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "private static readonly IServiceProtocol GrpcServiceProtocol ="
                  + " new GrpcProtocol().ForService($L);",
              SchemaGenerator.serviceSchemaAccessor(context, service));
          boolean hasStreaming =
              ops.stream().anyMatch(op -> isEventStreamOperation(context.model(), op));
          if (hasStreaming) {
            writer.write(
                "private static readonly IEventStreamServiceProtocol GrpcEventStreamServiceProtocol"
                    + " = (IEventStreamServiceProtocol)GrpcServiceProtocol;");
          }
          for (OperationShape op : ops) {
            if (isEventStreamOperation(context.model(), op)) {
              writeGrpcEventStreamProtocolField(sp, op);
              continue;
            }
            writer.write(
                "private static readonly IOperationProtocol<$L, $L> $LGrpcProtocol ="
                    + " GrpcServiceProtocol.ForOperation($L);",
                SchemaGenerator.operationShapeType(context, op.getInputShape()),
                SchemaGenerator.operationShapeType(context, op.getOutputShape()),
                CSharpNaming.typeName(op.getId().getName()),
                SchemaGenerator.operationSchemaAccessor(context, op));
          }
          writer.write("");

          writer.write(
              "public static IEndpointRouteBuilder Map$LGrpc(this IEndpointRouteBuilder endpoints)",
              contract);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                writer.write("");
                for (OperationShape op : ops) {
                  writeGrpcOperationMap(sp, op);
                  writer.write("");
                }
                writer.write("return endpoints;");
              });
        });
  }

  private void writeGrpcOperationMap(SymbolProvider sp, OperationShape op) {
    String opInterface = opHandlerName(op);
    // gRPC full method path: "/{namespace}.{Service}/{Operation}" — must match GrpcProtocol, which
    // derives it from the schema's raw shape ids.
    String uri =
        "/"
            + service.getId().getNamespace()
            + "."
            + service.getId().getName()
            + "/"
            + op.getId().getName();
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
          writeGrpcOperationBody(sp, op);
        });
  }

  private void writeGrpcOperationBody(SymbolProvider sp, OperationShape op) {
    if (isEventStreamOperation(context.model(), op)) {
      writeGrpcEventStreamOperationBody(sp, op);
      return;
    }

    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String methodName = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String opProtocol = CSharpNaming.typeName(op.getId().getName()) + "GrpcProtocol";

    String handlerCall;
    if (hasInput) {
      writer.write(
          "var smithyRequest = await SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync("
              + "httpContext, cancellationToken).ConfigureAwait(false);");
      writer.write("var input = $L.DeserializeRequest(smithyRequest);", opProtocol);
      handlerCall = "handler." + methodName + "(input, cancellationToken)";
    } else {
      handlerCall = "handler." + methodName + "(cancellationToken)";
    }

    String serializeResponse = opProtocol + ".SerializeResponse(output)";

    List<ShapeId> errorIds = new ArrayList<>(op.getErrors(service));
    errorIds.sort(Comparator.comparing(ShapeId::toString));
    if (!errorIds.isEmpty()) {
      writer.write("NSmithy.Http.SmithyHttpResponse smithyResponse;");
      writer.write("try");
      writer.openBlock(
          "{",
          "}",
          () -> {
            writeHandlerInvocation(handlerCall, hasOutput);
            writer.write("smithyResponse = $L;", serializeResponse);
          });
      for (ShapeId errId : errorIds) {
        writeErrorCatch(errId, opProtocol);
      }
    } else {
      writeHandlerInvocation(handlerCall, hasOutput);
      writer.write("var smithyResponse = $L;", serializeResponse);
    }

    writer.write(
        "await SmithyAspNetCoreProtocol.WriteSmithyGrpcResponseAsync(httpContext, smithyResponse,"
            + " cancellationToken).ConfigureAwait(false);");
  }

  private void writeGrpcEventStreamProtocolField(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputType = SchemaGenerator.operationShapeType(context, op.getInputShape());
    String outputType = SchemaGenerator.operationShapeType(context, op.getOutputShape());
    String operationSchema = SchemaGenerator.operationSchemaAccessor(context, op);
    if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
      writer.write(
          "private static readonly IBidirectionalEventStreamOperationProtocol<$L, $L>"
              + " $LGrpcProtocol ="
              + " GrpcEventStreamServiceProtocol.ForBidirectionalEventStreamOperation($L, $L, $L);",
          streamingEventType(sp, model, op.getInputShape()),
          streamingEventType(sp, model, op.getOutputShape()),
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()),
          streamingEventSchema(model, op.getOutputShape()));
    } else if (isOutputStreaming(model, op)) {
      writer.write(
          "private static readonly IServerEventStreamOperationProtocol<$L, $L> $LGrpcProtocol ="
              + " GrpcEventStreamServiceProtocol.ForServerEventStreamOperation($L, $L);",
          inputType,
          streamingEventType(sp, model, op.getOutputShape()),
          opName,
          operationSchema,
          streamingEventSchema(model, op.getOutputShape()));
    } else {
      writer.write(
          "private static readonly IClientEventStreamOperationProtocol<$L, $L> $LGrpcProtocol ="
              + " GrpcEventStreamServiceProtocol.ForClientEventStreamOperation($L, $L);",
          streamingEventType(sp, model, op.getInputShape()),
          outputType,
          opName,
          operationSchema,
          streamingEventSchema(model, op.getInputShape()));
    }
  }

  private void writeGrpcEventStreamOperationBody(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    String methodName = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String opProtocol = CSharpNaming.typeName(op.getId().getName()) + "GrpcProtocol";

    if (isInputStreaming(model, op) && isOutputStreaming(model, op)) {
      writer.write(
          "var smithyRequest = SmithyAspNetCoreProtocol.CreateSmithyGrpcEventStreamRequest("
              + "httpContext, cancellationToken);");
      writer.write(
          "var input = $L.DeserializeRequestEventsAsync(smithyRequest, cancellationToken);",
          opProtocol);
      writer.write("var output = handler.$L(input, cancellationToken);", methodName);
      writer.write(
          "var responseEvents = $L.SerializeResponseEventsAsync(output, cancellationToken);",
          opProtocol);
      writer.write(
          "await SmithyAspNetCoreProtocol.WriteSmithyGrpcEventStreamResponseAsync(httpContext,"
              + " responseEvents, cancellationToken).ConfigureAwait(false);");
    } else if (isOutputStreaming(model, op)) {
      boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
      if (hasInput) {
        writer.write(
            "var smithyRequest = await SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync("
                + "httpContext, cancellationToken).ConfigureAwait(false);");
        writer.write("var input = $L.DeserializeRequest(smithyRequest);", opProtocol);
        writer.write("var output = handler.$L(input, cancellationToken);", methodName);
      } else {
        writer.write("var output = handler.$L(cancellationToken);", methodName);
      }
      writer.write(
          "var responseEvents = $L.SerializeResponseEventsAsync(output, cancellationToken);",
          opProtocol);
      writer.write(
          "await SmithyAspNetCoreProtocol.WriteSmithyGrpcEventStreamResponseAsync(httpContext,"
              + " responseEvents, cancellationToken).ConfigureAwait(false);");
    } else {
      writer.write(
          "var smithyRequest = SmithyAspNetCoreProtocol.CreateSmithyGrpcEventStreamRequest("
              + "httpContext, cancellationToken);");
      writer.write(
          "var input = $L.DeserializeRequestEventsAsync(smithyRequest, cancellationToken);",
          opProtocol);
      writer.write(
          "var output = await handler.$L(input, cancellationToken).ConfigureAwait(false);",
          methodName);
      writer.write("var responseEvent = $L.SerializeResponse(output);", opProtocol);
      writer.write(
          "await SmithyAspNetCoreProtocol.WriteSmithyGrpcEventStreamResponseAsync(httpContext,"
              + " responseEvent, cancellationToken).ConfigureAwait(false);");
    }
  }

  // ---------------- descriptor ----------------

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

  // ---------------- ASP.NET Core endpoint extensions ----------------

  private void writeAspNetCoreExtensions(
      SymbolProvider sp, List<OperationShape> ops, String contract) {
    writer.write("public static class $LAspNetCoreExtensions", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
          // Build operation-bound protocols once from the (service, operation) schemas, exactly
          // like the generated client; the endpoint bodies then call them uniformly.
          writer.write(
              "private static readonly IServiceProtocol ServiceProtocol = new $L().ForService($L);",
              ProtocolSupport.protocolType(kind),
              SchemaGenerator.serviceSchemaAccessor(context, service));
          for (OperationShape op : ops) {
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
              "public static IEndpointRouteBuilder Map$LHttp(this IEndpointRouteBuilder endpoints)",
              contract);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                writer.write("");
                for (OperationShape op : ops) {
                  writeOperationMap(sp, op, contract);
                  writer.write("");
                }
                writer.write("return endpoints;");
              });
        });
  }

  private void writeOperationMap(SymbolProvider sp, OperationShape op, String contract) {
    String opInterface = opHandlerName(op);
    switch (kind) {
      case RPC_V2_CBOR -> writeRpcV2CborOperationMap(sp, op, opInterface);
      case SIMPLE_REST_JSON, REST_JSON_1, REST_XML -> writeRestOperationMap(sp, op, opInterface);
      default ->
          throw new IllegalStateException("Unsupported protocol for server codegen: " + kind);
    }
  }

  private void writeRpcV2CborOperationMap(
      SymbolProvider sp, OperationShape op, String opInterface) {
    // rpcv2Cbor uses a synthetic URI; operations have no @http trait.
    String uri = "/service/" + service.getId().getName() + "/operation/" + op.getId().getName();
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
          writeOperationBody(sp, op);
        });
  }

  private void writeRestOperationMap(SymbolProvider sp, OperationShape op, String opInterface) {
    HttpTrait http = op.expectTrait(HttpTrait.class);
    writer.openBlock(
        "endpoints.MapMethods($L, [$L], async (HttpContext httpContext, $L handler,"
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
          writeOperationBody(sp, op);
        });
  }

  private void writeOperationBody(SymbolProvider sp, OperationShape op) {
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    boolean rpc = kind == ProtocolSupport.Kind.RPC_V2_CBOR;
    String methodName = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String opProtocol = CSharpNaming.typeName(op.getId().getName()) + "Protocol";

    // Call the handler interface method directly — the operation-bound protocol carries the
    // serialization metadata, so a separate per-operation descriptor is unnecessary.
    String handlerCall;
    if (hasInput) {
      writer.write(
          "var smithyRequest = await SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync("
              + "httpContext, cancellationToken).ConfigureAwait(false);");
      writer.write("var input = $L.DeserializeRequest(smithyRequest);", opProtocol);
      handlerCall = "handler." + methodName + "(input, cancellationToken)";
    } else {
      handlerCall = "handler." + methodName + "(cancellationToken)";
    }

    String serializeResponse = opProtocol + ".SerializeResponse(output)";

    // Catch modeled errors and serialize them as protocol error responses. Supported for the
    // rpcv2Cbor and restJson1 protocols (restXml server error serialization is not yet wired up).
    List<ShapeId> errorIds = new ArrayList<>(op.getErrors(service));
    errorIds.sort(Comparator.comparing(ShapeId::toString));
    boolean catchErrors =
        !errorIds.isEmpty()
            && (rpc
                || kind == ProtocolSupport.Kind.SIMPLE_REST_JSON
                || kind == ProtocolSupport.Kind.REST_JSON_1);

    if (catchErrors) {
      writer.write("NSmithy.Http.SmithyHttpResponse smithyResponse;");
      writer.write("try");
      writer.openBlock(
          "{",
          "}",
          () -> {
            writeHandlerInvocation(handlerCall, hasOutput);
            writer.write("smithyResponse = $L;", serializeResponse);
          });
      for (ShapeId errId : errorIds) {
        writeErrorCatch(errId, opProtocol);
      }
    } else {
      writeHandlerInvocation(handlerCall, hasOutput);
      writer.write("var smithyResponse = $L;", serializeResponse);
    }

    writer.write(
        "await SmithyAspNetCoreProtocol.WriteSmithyHttpResponseAsync(httpContext, smithyResponse,"
            + " cancellationToken).ConfigureAwait(false);");
  }

  private void writeHandlerInvocation(String handlerCall, boolean hasOutput) {
    if (hasOutput) {
      writer.write("var output = await $L.ConfigureAwait(false);", handlerCall);
    } else {
      writer.write("await $L.ConfigureAwait(false);", handlerCall);
      writer.write("var output = SmithyUnit.Value;");
    }
  }

  private void writeErrorCatch(ShapeId errId, String opProtocol) {
    StructureShape err = context.model().expectShape(errId, StructureShape.class);
    String errType = CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(err));
    String errSchema = SchemaGenerator.shapeSchemaAccessor(context, err);
    int statusCode = httpErrorCode(err);
    writer.write("catch ($L error)", errType);
    writer.openBlock(
        "{",
        "}",
        () ->
            writer.write(
                "smithyResponse = $L.SerializeError($L, error, $L, $L);",
                opProtocol,
                errSchema,
                CSharpNaming.formatString(errId.toString()),
                statusCode));
  }

  private static int httpErrorCode(StructureShape err) {
    return err.getTrait(HttpErrorTrait.class)
        .map(HttpErrorTrait::getCode)
        .orElseGet(
            () ->
                err.getTrait(ErrorTrait.class).map(t -> t.isClientError() ? 400 : 500).orElse(500));
  }

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
          "if (!SmithyAspNetCoreProtocol.HasExpectedQueryLiteral(httpContext, $L, $L))",
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

  private String bodyProjectionConstructorArguments(List<MemberShape> bodyMembers) {
    List<String> args = new java.util.ArrayList<>();
    for (MemberShape m : bodyMembers) {
      String local = CSharpNaming.parameterName(m.getMemberName());
      if (ShapeSupport.isOptionalParameter(m)) {
        args.add(local);
      } else {
        args.add(
            local
                + " ?? throw new System.InvalidOperationException("
                + CSharpNaming.formatString("Missing required member '" + m.getMemberName() + "'.")
                + ")");
      }
    }
    return String.join(", ", args);
  }

  private String writeValueStatement(
      Shape target, String serializerVar, String schemaVar, String valueExpr) {
    return switch (target.getType()) {
      case BOOLEAN -> serializerVar + ".WriteBoolean(" + schemaVar + ", " + valueExpr + ");";
      case BYTE -> serializerVar + ".WriteByte(" + schemaVar + ", " + valueExpr + ");";
      case SHORT -> serializerVar + ".WriteShort(" + schemaVar + ", " + valueExpr + ");";
      case INTEGER -> serializerVar + ".WriteInteger(" + schemaVar + ", " + valueExpr + ");";
      case LONG -> serializerVar + ".WriteLong(" + schemaVar + ", " + valueExpr + ");";
      case FLOAT -> serializerVar + ".WriteFloat(" + schemaVar + ", " + valueExpr + ");";
      case DOUBLE -> serializerVar + ".WriteDouble(" + schemaVar + ", " + valueExpr + ");";
      case BIG_INTEGER -> serializerVar + ".WriteBigInteger(" + schemaVar + ", " + valueExpr + ");";
      case BIG_DECIMAL -> serializerVar + ".WriteBigDecimal(" + schemaVar + ", " + valueExpr + ");";
      case TIMESTAMP -> serializerVar + ".WriteTimestamp(" + schemaVar + ", " + valueExpr + ");";
      case STRING -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ");";
      case ENUM -> serializerVar + ".WriteString(" + schemaVar + ", " + valueExpr + ".Value);";
      case BLOB -> serializerVar + ".WriteBlob(" + schemaVar + ", " + valueExpr + ");";
      case DOCUMENT -> serializerVar + ".WriteDocument(" + schemaVar + ", " + valueExpr + ");";
      case INT_ENUM -> serializerVar + ".WriteInteger(" + schemaVar + ", (int)" + valueExpr + ");";
      case STRUCTURE -> serializerVar + ".WriteStruct(" + schemaVar + ", " + valueExpr + ");";
      case UNION, LIST, SET, MAP ->
          valueExpr + ".Serialize(" + serializerVar + ", " + schemaVar + ");";
      default ->
          throw new IllegalArgumentException(
              "Unsupported body projection member shape: " + target.getId());
    };
  }

  private static String stripTrailingSemicolon(String statement) {
    return statement.endsWith(";") ? statement.substring(0, statement.length() - 1) : statement;
  }

  private String readValueExpression(Shape target, String deserializerVar, String schemaVar) {
    return switch (target.getType()) {
      case BOOLEAN -> deserializerVar + ".ReadBoolean(" + schemaVar + ")";
      case BYTE -> deserializerVar + ".ReadByte(" + schemaVar + ")";
      case SHORT -> deserializerVar + ".ReadShort(" + schemaVar + ")";
      case INTEGER -> deserializerVar + ".ReadInteger(" + schemaVar + ")";
      case LONG -> deserializerVar + ".ReadLong(" + schemaVar + ")";
      case FLOAT -> deserializerVar + ".ReadFloat(" + schemaVar + ")";
      case DOUBLE -> deserializerVar + ".ReadDouble(" + schemaVar + ")";
      case BIG_INTEGER -> deserializerVar + ".ReadBigInteger(" + schemaVar + ")";
      case BIG_DECIMAL -> deserializerVar + ".ReadBigDecimal(" + schemaVar + ")";
      case TIMESTAMP -> deserializerVar + ".ReadTimestamp(" + schemaVar + ")";
      case STRING -> deserializerVar + ".ReadString(" + schemaVar + ")";
      case BLOB -> deserializerVar + ".ReadBlob(" + schemaVar + ")";
      case DOCUMENT -> deserializerVar + ".ReadDocument(" + schemaVar + ")";
      case ENUM, STRUCTURE, UNION, LIST, SET, MAP ->
          CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ".Deserialize("
              + deserializerVar
              + ")";
      case INT_ENUM ->
          "("
              + CSharpSymbolProvider.qualified(context.symbolProvider().toSymbol(target))
              + ")"
              + deserializerVar
              + ".ReadInteger("
              + schemaVar
              + ")";
      default ->
          throw new IllegalArgumentException(
              "Unsupported body projection member shape: " + target.getId());
    };
  }

  // ---------------- helpers ----------------

  private static String serviceContractName(String serviceTypeName) {
    return serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
  }

  private String opHandlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  private String serverOperationSignature(SymbolProvider sp, OperationShape op) {
    Model model = context.model();
    boolean supportsStreaming = ProtocolSupport.isGrpcService(service);
    boolean inputStreaming = supportsStreaming && isInputStreaming(model, op);
    boolean outputStreaming = supportsStreaming && isOutputStreaming(model, op);
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
    return ShapeSupport.isStreamingShape(model, op.getInputShape());
  }

  private boolean isOutputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isStreamingShape(model, op.getOutputShape());
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

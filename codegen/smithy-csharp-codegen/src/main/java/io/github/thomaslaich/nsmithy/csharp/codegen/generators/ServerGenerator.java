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
import java.util.Comparator;
import java.util.HashSet;
import java.util.List;
import java.util.Optional;
import java.util.Set;
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
import software.amazon.smithy.model.traits.HttpHeaderTrait;
import software.amazon.smithy.model.traits.HttpPrefixHeadersTrait;
import software.amazon.smithy.model.traits.HttpQueryTrait;
import software.amazon.smithy.model.traits.HttpTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ServerGenerator implements Runnable {

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;
  private final boolean rawRestJsonStringPayloads;
  private final ProtocolSupport.Kind kind;

  public ServerGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
    this.rawRestJsonStringPayloads =
        s.findTrait(io.github.thomaslaich.nsmithy.csharp.codegen.TraitIds.REST_JSON_1).isPresent();
    this.kind =
        ProtocolSupport.emitsHttpClient(s) ? ProtocolSupport.kindOf(s) : ProtocolSupport.Kind.REST_JSON;
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
      writer.addImport(RuntimeTypes.NSMITHY_PROTOCOLS_RESTJSON);
      writer.addImport(RuntimeTypes.NSMITHY_SERVER_ASPNETCORE);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_HTTP);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_ROUTING);
    }
    if (emitsGrpc) {
      writer.addImport(RuntimeTypes.GRPC_CORE);
      writer.addImport(RuntimeTypes.MS_ASPNETCORE_BUILDER);
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

    // gRPC adapter (binds the protoc-generated base to the IServiceHandler)
    if (emitsGrpc) {
      writeGrpcAdapter(sp, ops, contract, aggInterface);
      writer.write("");
      writeGrpcMapExtensions(contract, serviceTypeName);
    }
  }

  // ---------------- gRPC adapter ----------------

  private void writeGrpcAdapter(
      SymbolProvider sp, List<OperationShape> ops, String contract, String aggInterface) {
    String svcName = CSharpNaming.typeName(service.getId().getName());
    String grpcNs = grpcNamespace();
    String baseType = "global::" + grpcNs + "." + svcName + "." + svcName + "Base";
    String adapterName = svcName + "GrpcAdapter";
    writer.write("public sealed class $L : $L", adapterName, baseType);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("private readonly $L handler;", aggInterface);
          writer.write("");
          writer.write("public $L($L handler)", adapterName, aggInterface);
          writer.openBlock(
              "{",
              "}",
              () ->
                  writer.write(
                      "this.handler = handler ?? throw new"
                          + " System.ArgumentNullException(nameof(handler));"));
          writer.write("");
          for (OperationShape op : ops) {
            writeGrpcAdapterMethod(sp, op);
            writer.write("");
          }
        });
  }

  private void writeGrpcAdapterMethod(SymbolProvider sp, OperationShape op) {
    writeGrpcAdapterUnaryMethod(sp, op);
  }

  private void writeGrpcAdapterUnaryMethod(SymbolProvider sp, OperationShape op) {
    String operationName = CSharpNaming.typeName(op.getId().getName());
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String grpcInputType = grpcMessageType(op.getInputShape());
    String grpcOutputType = grpcMessageType(op.getOutputShape());
    writer.write(
        "public override async System.Threading.Tasks.Task<$L> $L($L request,"
            + " ServerCallContext context)",
        grpcOutputType,
        operationName,
        grpcInputType);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(request);");
          writer.write("System.ArgumentNullException.ThrowIfNull(context);");
          writer.write("");
          if (hasInput) {
            writer.write(
                "var smithyInput = $L;",
                GrpcConversions.grpcToSmithy(
                    sp,
                    context.model(),
                    context.model().expectShape(op.getInputShape()),
                    "request",
                    grpcNamespace()));
          }
          String invokeArgs = (hasInput ? "smithyInput, " : "") + "context.CancellationToken";
          if (hasOutput) {
            writer.write(
                "var smithyOutput = await handler.$LAsync($L).ConfigureAwait(false);",
                operationName,
                invokeArgs);
            writer.write(
                "return $L;",
                GrpcConversions.smithyToGrpc(
                    sp,
                    context.model(),
                    context.model().expectShape(op.getOutputShape()),
                    "smithyOutput",
                    grpcNamespace()));
          } else {
            writer.write(
                "await handler.$LAsync($L).ConfigureAwait(false);", operationName, invokeArgs);
            writer.write("return new Google.Protobuf.WellKnownTypes.Empty();");
          }
        });
  }

  private void writeGrpcMapExtensions(String contract, String serviceTypeName) {
    String adapterName = serviceTypeName + "GrpcAdapter";
    writer.write("public static class $LGrpcExtensions", contract);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write(
              "public static IEndpointRouteBuilder Map$LGrpc(this IEndpointRouteBuilder"
                  + " endpoints)",
              contract);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                writer.write("endpoints.MapGrpcService<$L>();", adapterName);
                writer.write("return endpoints;");
              });
        });
  }

  private String grpcMessageType(ShapeId id) {
    if (ShapeSupport.isUnit(id)) {
      return "Google.Protobuf.WellKnownTypes.Empty";
    }
    return "global::" + grpcNamespace() + "." + CSharpNaming.typeName(id.getName());
  }

  private String grpcNamespace() {
    return context.settings().csharpNamespace(service.getId().getNamespace()) + ".Grpc";
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
    HttpTrait http = op.expectTrait(HttpTrait.class);
    String opInterface = opHandlerName(op);
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
    String methodName = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String protocol = ProtocolSupport.functionalProtocolType(kind);
    String opSchema = SchemaGenerator.functionalOperationSchemaAccessor(context, op);

    // Call the handler interface method directly — the operation schema carries the
    // serialization metadata, so a separate per-operation descriptor is unnecessary.
    String handlerCall;
    if (hasInput) {
      writer.write(
          "var smithyRequest = await SmithyAspNetCoreProtocol.CreateSmithyHttpRequestAsync("
              + "httpContext, cancellationToken).ConfigureAwait(false);");
      writer.write("var input = $L.DeserializeRequest($L, smithyRequest);", protocol, opSchema);
      handlerCall = "handler." + methodName + "(input, cancellationToken)";
    } else {
      handlerCall = "handler." + methodName + "(cancellationToken)";
    }

    if (hasOutput) {
      writer.write("var output = await $L.ConfigureAwait(false);", handlerCall);
    } else {
      writer.write("await $L.ConfigureAwait(false);", handlerCall);
      writer.write("var output = SmithyUnit.Value;");
    }

    writer.write("var smithyResponse = $L.SerializeResponse($L, output);", protocol, opSchema);
    writer.write(
        "await SmithyAspNetCoreProtocol.WriteSmithyHttpResponseAsync(httpContext, smithyResponse,"
            + " cancellationToken).ConfigureAwait(false);");
  }

  private String routePattern(HttpTrait http) {
    String uri = http.getUri().toString();
    int queryIndex = uri.indexOf('?');
    return queryIndex >= 0 ? uri.substring(0, queryIndex) : uri;
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
    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String inputType =
        hasInput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getInputShape())))
            : null;
    String outputType =
        hasOutput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getOutputShape())))
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
}

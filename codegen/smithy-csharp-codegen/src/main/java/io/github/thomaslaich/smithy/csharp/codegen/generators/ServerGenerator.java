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
package io.github.thomaslaich.smithy.csharp.codegen.generators;

import io.github.thomaslaich.smithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.smithy.csharp.codegen.CSharpSymbolProvider;
import io.github.thomaslaich.smithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.smithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.smithy.csharp.codegen.support.AttributeEmitter;
import io.github.thomaslaich.smithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.smithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
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

    boolean emitsAspNet = ProtocolSupport.emitsAspNetCoreServer(service);
    boolean emitsGrpc = ProtocolSupport.isGrpcService(service);

    writer.addImport(RuntimeTypes.NSMITHY_CORE);
    writer.addImport(RuntimeTypes.NSMITHY_SERVER);
    writer.addImport(RuntimeTypes.MS_EXT_DI);
    if (emitsAspNet) {
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
      writer.openBlock(
          "public interface $L {",
          "}",
          opHandlerName(op),
          () -> writer.write("$L;", serverOperationSignature(sp, op)));
      writer.write("");
    }

    // Aggregate interface
    String inherits =
        ops.isEmpty()
            ? ""
            : " : " + ops.stream().map(this::opHandlerName).collect(Collectors.joining(", "));
    writer.write("public interface $L$L { }", aggInterface, inherits);
    writer.write("");

    // Descriptor
    writeDescriptor(sp, ops, contract, aggInterface);
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
    writer.openBlock(
        "public sealed class $L : $L {",
        "}",
        adapterName,
        baseType,
        () -> {
          writer.write("private readonly $L handler;", aggInterface);
          writer.write("");
          writer.openBlock(
              "public $L($L handler) {",
              "}",
              adapterName,
              aggInterface,
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
    String operationName = CSharpNaming.typeName(op.getId().getName());
    boolean hasInput = !op.getInputShape().equals(ShapeId.from("smithy.api#Unit"));
    boolean hasOutput = !op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"));
    String grpcInputType = grpcMessageType(op.getInputShape());
    String grpcOutputType = grpcMessageType(op.getOutputShape());
    writer.openBlock(
        "public override async System.Threading.Tasks.Task<$L> $L($L request,"
            + " ServerCallContext context) {",
        "}",
        grpcOutputType,
        operationName,
        grpcInputType,
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(request);");
          writer.write("System.ArgumentNullException.ThrowIfNull(context);");
          writer.write("");
          if (hasInput) {
            writer.write(
                "var smithyInput = $L;",
                GrpcConversions.grpcToSmithy(
                    sp, context.model().expectShape(op.getInputShape()), "request"));
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
    writer.openBlock(
        "public static class $LGrpcExtensions {",
        "}",
        contract,
        () ->
            writer.openBlock(
                "public static IEndpointRouteBuilder Map$LGrpc(this IEndpointRouteBuilder"
                    + " endpoints) {",
                "}",
                contract,
                () -> {
                  writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                  writer.write("endpoints.MapGrpcService<$L>();", adapterName);
                  writer.write("return endpoints;");
                }));
  }

  private String grpcMessageType(ShapeId id) {
    if (id.equals(ShapeId.from("smithy.api#Unit"))) {
      return "Google.Protobuf.WellKnownTypes.Empty";
    }
    return "global::" + grpcNamespace() + "." + CSharpNaming.typeName(id.getName());
  }

  private String grpcNamespace() {
    return context.settings().csharpNamespace(service.getId().getNamespace()) + ".Grpc";
  }

  // ---------------- descriptor ----------------

  private void writeDescriptor(
      SymbolProvider sp, List<OperationShape> ops, String contract, String aggInterface) {
    writer.openBlock(
        "public static class $LDescriptor {",
        "}",
        contract,
        () -> {
          for (OperationShape op : ops) {
            writeOperationDescriptor(sp, op);
            writer.write("");
          }
          writer.openBlock(
              "public static SmithyServiceDescriptor<$L> Service { get; } = new(",
              ");",
              aggInterface,
              () -> {
                writer.write("$L,", CSharpNaming.formatString(service.getId().toString()));
                writer.write("$L,", CSharpNaming.formatString(service.getId().getName()));
                writer.write("$L,", traitDescriptorsExpr(service.getAllTraits().values()));
                writer.openBlock(
                    "[",
                    "]",
                    () -> {
                      for (OperationShape op : ops) {
                        writer.write("$L,", CSharpNaming.typeName(op.getId().getName()));
                      }
                    });
              });
        });
  }

  private void writeOperationDescriptor(SymbolProvider sp, OperationShape op) {
    String descriptorName = CSharpNaming.typeName(op.getId().getName());
    String methodName = descriptorName + "Async";
    String opInterface = opHandlerName(op);
    boolean hasInput = !op.getInputShape().equals(ShapeId.from("smithy.api#Unit"));
    boolean hasOutput = !op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"));
    String inputType =
        hasInput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getInputShape())))
            : "SmithyUnit";
    String outputType =
        hasOutput
            ? CSharpSymbolProvider.qualified(
                sp.toSymbol(context.model().expectShape(op.getOutputShape())))
            : "SmithyUnit";

    writer.openBlock(
        "public static SmithyOperationDescriptor<$L, $L, $L> $L { get; } = new(",
        ");",
        opInterface,
        inputType,
        outputType,
        descriptorName,
        () -> {
          writer.write("$L,", CSharpNaming.formatString(op.getId().toString()));
          writer.write("$L,", CSharpNaming.formatString(op.getId().getName()));
          writer.write("$L,", traitDescriptorsExpr(op.getAllTraits().values()));
          if (hasInput && hasOutput) {
            writer.write(
                "static (handler, input, cancellationToken) => handler.$L(input,"
                    + " cancellationToken)",
                methodName);
          } else if (hasInput) {
            writer.openBlock(
                "static async (handler, input, cancellationToken) => {",
                "}",
                () -> {
                  writer.write(
                      "await handler.$L(input, cancellationToken).ConfigureAwait(false);",
                      methodName);
                  writer.write("return SmithyUnit.Value;");
                });
          } else if (hasOutput) {
            writer.write(
                "static (handler, _, cancellationToken) => handler.$L(cancellationToken)",
                methodName);
          } else {
            writer.openBlock(
                "static async (handler, _, cancellationToken) => {",
                "}",
                () -> {
                  writer.write(
                      "await handler.$L(cancellationToken).ConfigureAwait(false);", methodName);
                  writer.write("return SmithyUnit.Value;");
                });
          }
        });
  }

  private String traitDescriptorsExpr(
      java.util.Collection<? extends software.amazon.smithy.model.traits.Trait> traits) {
    if (traits.isEmpty()) return "[]";
    return "["
        + traits.stream()
            .sorted(Comparator.comparing(t -> t.toShapeId().toString()))
            .map(
                t -> {
                  Optional<String> v = traitValueLiteral(t);
                  String id = CSharpNaming.formatString(t.toShapeId().toString());
                  return v.isPresent()
                      ? "new SmithyTraitDescriptor("
                          + id
                          + ", "
                          + CSharpNaming.formatString(v.get())
                          + ")"
                      : "new SmithyTraitDescriptor(" + id + ")";
                })
            .collect(Collectors.joining(", "))
        + "]";
  }

  private static Optional<String> traitValueLiteral(software.amazon.smithy.model.traits.Trait t) {
    var n = t.toNode();
    return switch (n.getType()) {
      case BOOLEAN -> Optional.of(Boolean.toString(n.expectBooleanNode().getValue()));
      case NUMBER -> Optional.of(n.expectNumberNode().getValue().toString());
      case STRING -> Optional.of(n.expectStringNode().getValue());
      default -> Optional.empty();
    };
  }

  // ---------------- DI registration ----------------

  private void writeServerExtensions(
      List<OperationShape> ops, String contract, String aggInterface) {
    writer.openBlock(
        "public static class $LServerExtensions {",
        "}",
        contract,
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
    writer.openBlock(
        "public static class $LAspNetCoreExtensions {",
        "}",
        contract,
        () -> {
          writer.openBlock(
              "public static IEndpointRouteBuilder Map$LHttp(this IEndpointRouteBuilder endpoints)"
                  + " {",
              "}",
              contract,
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoints);");
                writer.write("");
                for (OperationShape op : ops) {
                  writeOperationMap(sp, op, contract);
                  writer.write("");
                }
                writer.write("return endpoints;");
              });
          // Bound response writers
          Set<ShapeId> emittedWriters = new HashSet<>();
          for (OperationShape op : ops) {
            if (op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"))) continue;
            if (!emittedWriters.add(op.getOutputShape())) continue;
            StructureShape output =
                context.model().expectShape(op.getOutputShape(), StructureShape.class);
            if (!ClientGenerator.hasResponseBindings(output)) continue;
            writeBoundResponseWriter(sp, output);
            writer.write("");
          }
          // Body projection types (input + output bound)
          writeAspNetCoreBodyProjectionTypes(sp, ops);
        });
  }

  private void writeOperationMap(SymbolProvider sp, OperationShape op, String contract) {
    HttpTrait http = op.expectTrait(HttpTrait.class);
    String opInterface = opHandlerName(op);
    writer.openBlock(
        "endpoints.MapMethods($L, [$L], async (HttpContext httpContext, $L handler,"
            + " System.Threading.CancellationToken cancellationToken) => {",
        "});",
        CSharpNaming.formatString(http.getUri().toString()),
        CSharpNaming.formatString(http.getMethod()),
        opInterface,
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(httpContext);");
          writer.write("System.ArgumentNullException.ThrowIfNull(handler);");
          writer.write("");
          writeOperationBody(sp, op, contract);
        });
  }

  private void writeOperationBody(SymbolProvider sp, OperationShape op, String contract) {
    boolean hasInput = !op.getInputShape().equals(ShapeId.from("smithy.api#Unit"));
    boolean hasOutput = !op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"));
    String descriptorAccess =
        contract + "Descriptor." + CSharpNaming.typeName(op.getId().getName());

    if (hasInput) {
      StructureShape input = context.model().expectShape(op.getInputShape(), StructureShape.class);
      String inputType = CSharpSymbolProvider.qualified(sp.toSymbol(input));
      writeInputBinding(sp, input, inputType);
      writer.write(
          "$L $L.InvokeAsync(handler, input, cancellationToken).ConfigureAwait(false);",
          hasOutput ? "var output = await" : "await",
          descriptorAccess);
    } else {
      writer.write(
          "$L $L.InvokeAsync(handler, SmithyUnit.Value, cancellationToken).ConfigureAwait(false);",
          hasOutput ? "var output = await" : "await",
          descriptorAccess);
    }
    if (!hasOutput) {
      writer.write("httpContext.Response.StatusCode = StatusCodes.Status204NoContent;");
    } else {
      StructureShape output =
          context.model().expectShape(op.getOutputShape(), StructureShape.class);
      if (ClientGenerator.hasResponseBindings(output)) {
        writer.write(
            "await WriteBoundResponseAsync(httpContext, output,"
                + " cancellationToken).ConfigureAwait(false);");
      } else {
        writer.write(
            "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, output,"
                + " cancellationToken).ConfigureAwait(false);");
      }
    }
  }

  private void writeInputBinding(SymbolProvider sp, StructureShape input, String inputType) {
    List<MemberShape> members = ShapeSupport.constructorMembers(input);
    List<MemberShape> bodyMembers =
        members.stream().filter(ShapeSupport::isHttpBody).collect(Collectors.toList());
    if (members.isEmpty()) {
      writer.write("var input = new $L();", inputType);
      return;
    }
    String bodyVar = null;
    if (!bodyMembers.isEmpty()) {
      boolean requiresBody = bodyMembers.stream().anyMatch(this::isRequiredHttpInputMember);
      String bodyType = ClientGenerator.bodyProjectionName(input);
      if (requiresBody) {
        writer.write(
            "var body = await"
                + " SmithyAspNetCoreProtocol.ReadRequiredJsonRequestBodyAsync<$L>(httpContext,"
                + " cancellationToken).ConfigureAwait(false);",
            bodyType);
      } else {
        writer.write(
            "var body = await SmithyAspNetCoreProtocol.ReadJsonRequestBodyAsync<$L>(httpContext,"
                + " cancellationToken).ConfigureAwait(false);",
            bodyType);
      }
      writer.write("");
      bodyVar = "body";
    }
    final String bv = bodyVar;
    writer.openBlock(
        "var input = new $L(",
        ");",
        inputType,
        () -> {
          for (int i = 0; i < members.size(); i++) {
            writer.write(
                "$L$L",
                inputMemberExpression(sp, input, members.get(i), bv),
                i == members.size() - 1 ? "" : ",");
          }
        });
  }

  private boolean isRequiredHttpInputMember(MemberShape m) {
    if (ShapeSupport.isHttpLabel(m)) return true;
    return ShapeSupport.isRequired(m);
  }

  private String inputMemberExpression(
      SymbolProvider sp, StructureShape input, MemberShape m, String bodyVar) {
    String memberType = ShapeSupport.parameterTypeExpr(sp, m);
    boolean required = isRequiredHttpInputMember(m);
    if (ShapeSupport.isHttpLabel(m)) {
      return "SmithyAspNetCoreProtocol.GetRouteValue<"
          + memberType
          + ">(httpContext, "
          + CSharpNaming.formatString(m.getMemberName())
          + ")";
    }
    if (ShapeSupport.isHttpQuery(m)) {
      String qn = m.expectTrait(HttpQueryTrait.class).getValue();
      return required
          ? "SmithyAspNetCoreProtocol.GetRequiredQueryValue<"
              + memberType
              + ">(httpContext, "
              + CSharpNaming.formatString(qn)
              + ")"
          : "SmithyAspNetCoreProtocol.GetQueryValue<"
              + memberType
              + ">(httpContext, "
              + CSharpNaming.formatString(qn)
              + ")";
    }
    if (ShapeSupport.isHttpQueryParams(m)) {
      String excluded =
          ShapeSupport.sortedMembers(input).stream()
              .filter(ShapeSupport::isHttpQuery)
              .map(qm -> CSharpNaming.formatString(qm.expectTrait(HttpQueryTrait.class).getValue()))
              .collect(Collectors.joining(", "));
      String expr =
          "SmithyAspNetCoreProtocol.GetQueryParams<"
              + memberType
              + ">(httpContext, ["
              + excluded
              + "])";
      return required ? expr + "!" : expr;
    }
    if (ShapeSupport.isHttpHeader(m)) {
      String name = m.expectTrait(HttpHeaderTrait.class).getValue();
      return required
          ? "SmithyAspNetCoreProtocol.GetRequiredHeaderValue<"
              + memberType
              + ">(httpContext, "
              + CSharpNaming.formatString(name)
              + ")"
          : "SmithyAspNetCoreProtocol.GetHeaderValue<"
              + memberType
              + ">(httpContext, "
              + CSharpNaming.formatString(name)
              + ")";
    }
    if (ShapeSupport.isHttpPrefixHeaders(m)) {
      String prefix = m.expectTrait(HttpPrefixHeadersTrait.class).getValue();
      String expr =
          "SmithyAspNetCoreProtocol.GetPrefixedHeaders<"
              + memberType
              + ">(httpContext, "
              + CSharpNaming.formatString(prefix)
              + ")";
      return required ? expr + "!" : expr;
    }
    if (ShapeSupport.isHttpPayload(m)) {
      return "await SmithyAspNetCoreProtocol.ReadJsonRequestBodyAsync<"
          + memberType
          + ">(httpContext, cancellationToken).ConfigureAwait(false)";
    }
    if (bodyVar != null) {
      return bodyVar + "." + CSharpNaming.propertyName(m.getMemberName());
    }
    throw new RuntimeException("Body member without projection: " + m.getId());
  }

  private void writeBoundResponseWriter(SymbolProvider sp, StructureShape output) {
    String outputType = CSharpSymbolProvider.qualified(sp.toSymbol(output));
    writer.openBlock(
        "private static async System.Threading.Tasks.Task WriteBoundResponseAsync(HttpContext"
            + " httpContext, $L output, System.Threading.CancellationToken cancellationToken) {",
        "}",
        outputType,
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(output);");
          for (MemberShape m : ShapeSupport.sortedMembers(output)) {
            if (ShapeSupport.isHttpResponseCode(m)) {
              writer.write(
                  "SmithyAspNetCoreProtocol.SetStatusCode(httpContext, output.$L);",
                  CSharpNaming.propertyName(m.getMemberName()));
            }
          }
          for (MemberShape m : ShapeSupport.sortedMembers(output)) {
            if (ShapeSupport.isHttpHeader(m)) {
              String name = m.expectTrait(HttpHeaderTrait.class).getValue();
              writer.write(
                  "SmithyAspNetCoreProtocol.AddResponseHeader(httpContext, $L, output.$L);",
                  CSharpNaming.formatString(name),
                  CSharpNaming.propertyName(m.getMemberName()));
            }
          }
          for (MemberShape m : ShapeSupport.sortedMembers(output)) {
            if (ShapeSupport.isHttpPrefixHeaders(m)) {
              String prefix = m.expectTrait(HttpPrefixHeadersTrait.class).getValue();
              writer.write(
                  "SmithyAspNetCoreProtocol.AddPrefixedResponseHeaders(httpContext, $L,"
                      + " output.$L);",
                  CSharpNaming.formatString(prefix),
                  CSharpNaming.propertyName(m.getMemberName()));
            }
          }
          Optional<MemberShape> payload =
              ShapeSupport.sortedMembers(output).stream()
                  .filter(ShapeSupport::isHttpPayload)
                  .findFirst();
          if (payload.isPresent()) {
            writer.write(
                "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, output.$L,"
                    + " cancellationToken).ConfigureAwait(false);",
                CSharpNaming.propertyName(payload.get().getMemberName()));
            return;
          }
          List<MemberShape> bodyMembers =
              ShapeSupport.sortedMembers(output).stream()
                  .filter(m -> ShapeSupport.isHttpBody(m) && !ShapeSupport.isHttpResponseCode(m))
                  .collect(Collectors.toList());
          if (bodyMembers.isEmpty()) return;
          writer.openBlock(
              "var responseBody = new $L(",
              ");",
              ClientGenerator.bodyProjectionName(output),
              () -> {
                for (int i = 0; i < bodyMembers.size(); i++) {
                  writer.write(
                      "output.$L$L",
                      CSharpNaming.propertyName(bodyMembers.get(i).getMemberName()),
                      i == bodyMembers.size() - 1 ? "" : ",");
                }
              });
          writer.write(
              "await SmithyAspNetCoreProtocol.WriteJsonResponseAsync(httpContext, responseBody,"
                  + " cancellationToken).ConfigureAwait(false);");
        });
  }

  private void writeAspNetCoreBodyProjectionTypes(SymbolProvider sp, List<OperationShape> ops) {
    Set<ShapeId> emitted = new HashSet<>();
    for (OperationShape op : ops) {
      if (!op.getInputShape().equals(ShapeId.from("smithy.api#Unit"))
          && emitted.add(op.getInputShape())) {
        StructureShape input =
            context.model().expectShape(op.getInputShape(), StructureShape.class);
        List<MemberShape> bodyMembers =
            ShapeSupport.constructorMembers(input).stream()
                .filter(ShapeSupport::isHttpBody)
                .collect(Collectors.toList());
        if (!bodyMembers.isEmpty()) {
          writer.write("");
          writeBodyProjectionType(sp, input, bodyMembers);
        }
      }
      if (!op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"))
          && emitted.add(op.getOutputShape())) {
        StructureShape output =
            context.model().expectShape(op.getOutputShape(), StructureShape.class);
        if (ClientGenerator.hasResponseBindings(output)) {
          List<MemberShape> bodyMembers = ClientGenerator.responseBodyMembers(output);
          if (!bodyMembers.isEmpty()) {
            writer.write("");
            writeBodyProjectionType(sp, output, bodyMembers);
          }
        }
      }
    }
  }

  private void writeBodyProjectionType(
      SymbolProvider sp, StructureShape shape, List<MemberShape> bodyMembers) {
    String typeName = ClientGenerator.bodyProjectionName(shape);
    AttributeEmitter.writeShapeAttributes(writer, shape);
    writer.openBlock(
        "private sealed class $L {",
        "}",
        typeName,
        () -> {
          writer.write("");
          writer.openBlock(
              "public $L(",
              ") {",
              typeName,
              () -> {
                for (int i = 0; i < bodyMembers.size(); i++) {
                  MemberShape m = bodyMembers.get(i);
                  String type = ShapeSupport.memberTypeExpr(sp, m, ShapeSupport.isNullable(m));
                  writer.write(
                      "$L $L$L",
                      type,
                      CSharpNaming.parameterName(m.getMemberName()),
                      i == bodyMembers.size() - 1 ? "" : ",");
                }
              });
          writer.indent();
          for (MemberShape m : bodyMembers) {
            writer.write(
                "$L = $L;",
                CSharpNaming.propertyName(m.getMemberName()),
                CSharpNaming.parameterName(m.getMemberName()));
          }
          writer.dedent();
          writer.write("}");
          writer.write("");
          for (MemberShape m : bodyMembers) {
            String type = ShapeSupport.memberTypeExpr(sp, m, ShapeSupport.isNullable(m));
            AttributeEmitter.writeMemberAttributes(
                writer, m, ShapeSupport.isSparseTarget(context.model(), m));
            writer.write(
                "public $L $L { get; }", type, CSharpNaming.propertyName(m.getMemberName()));
            writer.write("");
          }
        });
  }

  // ---------------- helpers ----------------

  private static String serviceContractName(String serviceTypeName) {
    return serviceTypeName.endsWith("Service") ? serviceTypeName : serviceTypeName + "Service";
  }

  private String opHandlerName(OperationShape op) {
    return "I" + CSharpNaming.typeName(op.getId().getName()) + "Handler";
  }

  private String serverOperationSignature(SymbolProvider sp, OperationShape op) {
    boolean hasInput = !op.getInputShape().equals(ShapeId.from("smithy.api#Unit"));
    boolean hasOutput = !op.getOutputShape().equals(ShapeId.from("smithy.api#Unit"));
    String name = CSharpNaming.typeName(op.getId().getName()) + "Async";
    String returnType =
        hasOutput
            ? "System.Threading.Tasks.Task<"
                + CSharpSymbolProvider.qualified(
                    sp.toSymbol(context.model().expectShape(op.getOutputShape())))
                + ">"
            : "System.Threading.Tasks.Task";
    String params =
        hasInput
            ? CSharpSymbolProvider.qualified(
                    sp.toSymbol(context.model().expectShape(op.getInputShape())))
                + " input, "
            : "";
    return returnType
        + " "
        + name
        + "("
        + params
        + "System.Threading.CancellationToken cancellationToken = default)";
  }
}

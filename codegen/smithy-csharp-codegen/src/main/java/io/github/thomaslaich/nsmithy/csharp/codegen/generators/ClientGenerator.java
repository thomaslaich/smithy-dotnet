/*
 * Renders the generated C# service client and client interface.
 * Protocol-specific wire behavior lives behind IServiceProtocol and the bound operation protocols;
 * this generator wires those protocols into idiomatic client methods and constructors.
 */
package io.github.thomaslaich.nsmithy.csharp.codegen.generators;

import io.github.thomaslaich.nsmithy.csharp.codegen.CSharpNaming;
import io.github.thomaslaich.nsmithy.csharp.codegen.GenerationContext;
import io.github.thomaslaich.nsmithy.csharp.codegen.RuntimeTypes;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ProtocolSupport.Kind;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.stream.Collectors;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.model.knowledge.PaginatedIndex;
import software.amazon.smithy.model.knowledge.PaginationInfo;
import software.amazon.smithy.model.knowledge.ServiceIndex;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.MemberShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.model.shapes.StructureShape;
import software.amazon.smithy.model.traits.DocumentationTrait;
import software.amazon.smithy.model.traits.EndpointTrait;
import software.amazon.smithy.model.traits.IdempotencyTokenTrait;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class ClientGenerator implements Runnable {

  private static final ShapeId GLACIER_SERVICE = ShapeId.from("com.amazonaws.glacier#Glacier");

  private final GenerationContext context;
  private final CSharpWriter writer;
  private final ServiceShape service;

  public ClientGenerator(GenerationContext c, CSharpWriter w, ServiceShape s) {
    this.context = c;
    this.writer = w;
    this.service = s;
  }

  @Override
  public void run() {
    Model model = context.model();
    TopDownIndex idx = TopDownIndex.of(model);
    List<OperationShape> operations =
        idx.getContainedOperations(service).stream()
            .sorted(Comparator.comparing(o -> o.getId().toString()))
            .collect(Collectors.toList());
    List<Kind> kinds = ProtocolSupport.declaredKinds(service);
    if (kinds.contains(Kind.GRPC)) {
      operations.forEach(op -> ShapeSupport.requireGrpcEventStreamWrapperIsFlattenable(model, op));
    }

    String typeName = CSharpNaming.typeName(service.getId().getName()) + "Client";
    String interfaceName = "I" + typeName;

    // Interface — IDisposable so a client that owns its HttpClient is released via `using` or DI.
    writer.writeXmlDocs(service);
    writer.write("public interface $L : $T", interfaceName, RuntimeTypes.I_DISPOSABLE);
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("");
          for (OperationShape op : operations) {
            writer.writeXmlDocs(op, operationParameterDocs(model, op));
            writer.write("$L;", operationSignature(writer, context, op));
            paginationInfo(context, service, op)
                .ifPresent(
                    info -> {
                      writer.writeXmlDocs(op, operationParameterDocs(model, op));
                      writer.write("$L;", paginatorPagesSignature(writer, context, op));
                      paginatorItemsSignature(writer, context, info)
                          .ifPresent(
                              signature -> {
                                writer.writeXmlDocs(op, operationParameterDocs(model, op));
                                writer.write("$L;", signature);
                              });
                    });
          }
        });
    writer.write("");

    // Services with no supported protocol get the interface only; there is nothing to wire.
    if (kinds.isEmpty()) {
      return;
    }

    // The generated client only names the primary protocol (the default constructor argument);
    // callers selecting another declared protocol reference its namespace from their own code.

    writeClient(model, operations, typeName, interfaceName, kinds);
  }

  static String httpVersionPreferenceLiteral(
      CSharpWriter writer, Optional<ProtocolSupport.HttpVersionPreference> preference) {
    if (preference.isEmpty()) {
      return "null";
    }
    String version =
        switch (preference.get().alpnId()) {
          case "h3" -> writer.typeName(RuntimeTypes.HTTP_VERSION) + ".Version30";
          case "h2" -> writer.typeName(RuntimeTypes.HTTP_VERSION) + ".Version20";
          case "http/1.1" -> writer.typeName(RuntimeTypes.HTTP_VERSION) + ".Version11";
          default -> throw new IllegalArgumentException(preference.get().alpnId());
        };
    return ("new " + writer.typeName(RuntimeTypes.SMITHY_HTTP_VERSION_PREFERENCE) + "(")
        + version
        + ", allowDowngrade: "
        + preference.get().allowDowngrade()
        + ")";
  }

  // ---------------- paginators ----------------

  /**
   * Resolved pagination for an operation: present when the operation carries {@code @paginated}
   * (merged with the service-level defaults). Event-stream operations are never paginated. Static
   * (like the signature helpers below) so FakeClientGenerator renders the same paginator surface.
   */
  static Optional<PaginationInfo> paginationInfo(
      GenerationContext context, ServiceShape service, OperationShape op) {
    if (isEventStreamOperation(context.model(), op)) {
      return Optional.empty();
    }
    return PaginatedIndex.of(context.model()).getPaginationInfo(service, op);
  }

  static String paginatorPagesSignature(
      CSharpWriter writer, GenerationContext context, OperationShape op) {
    SymbolProvider sp = context.symbolProvider();
    Model model = context.model();
    String inputType = writer.typeName(sp.toSymbol(model.expectShape(op.getInputShape())));
    String outputType = writer.typeName(sp.toSymbol(model.expectShape(op.getOutputShape())));
    return writer.typeName(RuntimeTypes.I_ASYNC_ENUMERABLE)
        + "<"
        + outputType
        + "> "
        + CSharpNaming.typeName(op.getId().getName())
        + "PagesAsync("
        + inputType
        + " input, "
        + writer.typeName(RuntimeTypes.CANCELLATION_TOKEN)
        + " cancellationToken = default)";
  }

  /**
   * The items-level paginator signature, present when {@code @paginated} names an {@code items}
   * member that resolves to a list. Map-valued items are rare and not generated yet — the pages
   * paginator still covers them.
   */
  static Optional<String> paginatorItemsSignature(
      CSharpWriter writer, GenerationContext context, PaginationInfo info) {
    return paginatorItemElementType(writer, context, info)
        .map(
            elementType ->
                writer.typeName(RuntimeTypes.I_ASYNC_ENUMERABLE)
                    + "<"
                    + elementType
                    + "> "
                    + CSharpNaming.typeName(info.getOperation().getId().getName())
                    + "ItemsAsync("
                    + writer.typeName(
                        context
                            .symbolProvider()
                            .toSymbol(
                                context.model().expectShape(info.getOperation().getInputShape())))
                    + " input, "
                    + writer.typeName(RuntimeTypes.CANCELLATION_TOKEN)
                    + " cancellationToken = default)");
  }

  /** Renders a null-safe property access chain for a member path, e.g. {@code output.A?.B}. */
  static String memberPathExpr(String root, List<MemberShape> path) {
    StringBuilder expr = new StringBuilder(root);
    for (int i = 0; i < path.size(); i++) {
      expr.append(i == 0 ? "." : "?.")
          .append(CSharpNaming.propertyName(path.get(i).getMemberName()));
    }
    return expr.toString();
  }

  // =====================================================================
  // shared helpers
  // =====================================================================

  public static List<MemberShape> responseBodyMembers(StructureShape output) {
    return ShapeSupport.constructorMembers(output).stream()
        .filter(
            m ->
                !ShapeSupport.isHttpHeader(m)
                    && !ShapeSupport.isHttpPrefixHeaders(m)
                    && !ShapeSupport.isHttpResponseCode(m)
                    && !ShapeSupport.isHttpPayload(m))
        .collect(Collectors.toList());
  }

  /** Adds [EnumeratorCancellation] to a paginator signature's cancellation-token parameter. */
  static String withEnumeratorCancellation(CSharpWriter writer, String signature) {
    return signature.replace(
        writer.typeName(RuntimeTypes.CANCELLATION_TOKEN) + " cancellationToken",
        "["
            + writer.attributeName(RuntimeTypes.ENUMERATOR_CANCELLATION_ATTRIBUTE)
            + "]"
            + " "
            + writer.typeName(RuntimeTypes.CANCELLATION_TOKEN)
            + " cancellationToken");
  }

  static String operationSignature(
      CSharpWriter writer, GenerationContext context, OperationShape op) {
    SymbolProvider sp = context.symbolProvider();
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
    return returnType
        + " "
        + name
        + "("
        + params
        + writer.typeName(RuntimeTypes.CANCELLATION_TOKEN)
        + " cancellationToken = default)";
  }

  // =====================================================================
  // client
  // =====================================================================

  private void writeClient(
      Model model,
      List<OperationShape> operations,
      String typeName,
      String interfaceName,
      List<Kind> kinds) {
    String primaryProtocol = writer.typeName(ProtocolSupport.protocolType(kinds.get(0)));
    String modeledHttpVersionPreference =
        httpVersionPreferenceLiteral(
            writer,
            ProtocolSupport.httpVersionPreference(
                service, kinds.get(0), ProtocolSupport.hasEventStreamOperations(model, service)));
    String serviceSchema = SchemaGenerator.serviceSchemaAccessor(writer, context, service);
    // The idempotency-token provider is only stored/used when an operation has a nullable
    // @idempotencyToken member; emitting the field unconditionally would be an unused private
    // field.
    boolean needsRuntime = operations.stream().anyMatch(op -> canBindOperation(op));
    boolean needsIdempotency =
        operations.stream().anyMatch(op -> operationCanDefaultIdempotencyToken(model, op));
    writer.writeXmlDocs(service);
    writer.write("public sealed class $L : $L", typeName, interfaceName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (needsRuntime) {
            writer.write("private readonly $T runtime;", RuntimeTypes.SMITHY_CLIENT_RUNTIME);
            // The environment owns only resources created by the runtime factory.
            writer.write(
                "private readonly $T environment;", RuntimeTypes.SMITHY_HTTP_CLIENT_ENVIRONMENT);
          }
          if (needsIdempotency) {
            writer.write(
                "private readonly $T<string> idempotencyTokenProvider;", RuntimeTypes.FUNC);
          }
          // The protocol is bound at construction; per-operation protocols are built once from it.
          for (OperationShape op : operations) {
            if (!canBindOperation(op)) {
              continue;
            }
            writer.write(
                "private readonly $T<$L, $L> $LBinding;",
                RuntimeTypes.SMITHY_OPERATION_BINDING,
                SchemaGenerator.operationShapeType(writer, context, op.getInputShape()),
                SchemaGenerator.operationShapeType(writer, context, op.getOutputShape()),
                CSharpNaming.typeName(op.getId().getName()));
          }
          writer.write("");

          writeConstructors(
              typeName,
              serviceSchema,
              primaryProtocol,
              modeledHttpVersionPreference,
              needsRuntime,
              needsIdempotency,
              operations);

          // Every auth scheme that can be effective for a service operation. This includes schemes
          // introduced solely by an operation-level @auth override, so constructor validation does
          // not reject a client configured specifically for such an operation.
          writer.write(
              "private static readonly $T<string> ModeledAuthSchemes = $L;",
              RuntimeTypes.I_READ_ONLY_LIST,
              modeledAuthSchemesLiteral());
          writer.write("");

          if (needsIdempotency) {
            writer.write("");
            writer.write(
                "private static string DefaultIdempotencyToken() => $T.NewGuid().ToString();",
                RuntimeTypes.GUID);
          }
          writer.write("");

          // Disposes the HttpClient the client created itself; a no-op when the caller supplied the
          // HttpClient or runtime, so injected transports are never
          // closed.
          if (needsRuntime) {
            writer.write("public void Dispose() => environment.Dispose();");
          } else {
            writer.write("public void Dispose() { }");
          }
          writer.write("");

          for (OperationShape op : operations) {
            writeOperationMethod(model, op);
            paginationInfo(context, service, op).ifPresent(info -> writePaginatorMethods(op, info));
          }
        });
    writer.write("");
    writeConfigClass(typeName);
  }

  private void writeConstructors(
      String typeName,
      String serviceSchema,
      String primaryProtocol,
      String modeledHttpVersionPreference,
      boolean needsRuntime,
      boolean needsIdempotency,
      List<OperationShape> operations) {
    writer.pushState();
    try {
      writer.putContext("client", typeName);
      writer.putContext("uri", RuntimeTypes.URI);
      writer.putContext("httpClient", RuntimeTypes.HTTP_CLIENT);
      writer.putContext("runtime", RuntimeTypes.SMITHY_CLIENT_RUNTIME);
      writer.putContext("environment", RuntimeTypes.SMITHY_HTTP_CLIENT_ENVIRONMENT);
      writer.putContext("argumentNullException", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
      writer.putContext("serviceSchema", serviceSchema);
      writer.putContext("protocol", primaryProtocol);
      writer.putContext(
          "ownedInitialization",
          writer.consumer(
              w -> {
                if (needsRuntime) {
                  writeEnvironmentCreation(
                      serviceSchema, primaryProtocol, modeledHttpVersionPreference, "null");
                }
                writeConstructorBindings(needsRuntime, needsIdempotency, operations);
              }));
      writer.putContext(
          "httpClientInitialization",
          writer.consumer(
              w -> {
                if (needsRuntime) {
                  w.write("config ??= new ${client:L}Config();");
                  writeEnvironmentCreation(
                      serviceSchema, primaryProtocol, modeledHttpVersionPreference, "httpClient");
                }
                writeConstructorBindings(needsRuntime, needsIdempotency, operations);
              }));
      writer.putContext(
          "bindings",
          writer.consumer(
              w -> writeConstructorBindings(needsRuntime, needsIdempotency, operations)));

      // The explicit endpoint wins over config.Endpoint. The private constructor receives a copy.
      writer.write(
          """
          public ${client:L}(${uri:T} endpoint, ${client:L}Config? config = null) : this(WithEndpoint(endpoint, config))
          {
          }

          private ${client:L}(${client:L}Config config)
          {
              ${argumentNullException:T}.ThrowIfNull(config);
              ${ownedInitialization:C|}
          }

          public ${client:L}(${httpClient:T} httpClient, ${client:L}Config? config = null)
          {
              ${argumentNullException:T}.ThrowIfNull(httpClient);
              ${httpClientInitialization:C|}
          }
          """);
      writer.write("");

      if (needsRuntime) {
        // An injected runtime already owns its transport and interceptor pipeline.
        // Only Protocol and IdempotencyTokenProvider are read from config in this overload.
        writer.write(
            """
            public ${client:L}(${runtime:T} runtime, ${client:L}Config? config = null)
            {
                config ??= new ${client:L}Config();
                this.environment = ${environment:T}.FromRuntime(${serviceSchema:L}, runtime, config, static () => new ${protocol:L}());
                this.runtime = environment.Runtime;
                var serviceProtocol = environment.ServiceProtocol;
                ${bindings:C|}
            }
            """);
        writer.write("");
      }

      // Copy before assigning the endpoint so a config shared by callers remains unchanged.
      writer.write(
          """
          private static ${client:L}Config WithEndpoint(${uri:T} endpoint, ${client:L}Config? config)
          {
              ${argumentNullException:T}.ThrowIfNull(endpoint);
              var copy = config is null ? new ${client:L}Config() : new ${client:L}Config(config);
              copy.Endpoint = endpoint;
              return copy;
          }
          """);
      writer.write("");
    } finally {
      writer.popState();
    }
  }

  private void writeConstructorBindings(
      boolean needsRuntime, boolean needsIdempotency, List<OperationShape> operations) {
    writeIdempotencyAssignment(needsIdempotency);
    if (needsRuntime) {
      writeSafeOperationBindings(operations);
    }
  }

  private void writeSafeOperationBindings(List<OperationShape> operations) {
    writer.write("try");
    writer.openBlock("{", "}", () -> writeOperationBindings(operations));
    writer.write("catch");
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("environment.Dispose();");
          writer.write("throw;");
        });
  }

  private void writeEnvironmentCreation(
      String serviceSchema, String primaryProtocol, String preference, String httpClient) {
    writer.write(
        "this.environment = $T.Create($L, config, static () => new $L(), ModeledAuthSchemes, $L,"
            + " $L);",
        RuntimeTypes.SMITHY_HTTP_CLIENT_ENVIRONMENT,
        serviceSchema,
        primaryProtocol,
        preference,
        httpClient);
    writer.write("this.runtime = environment.Runtime;");
    writer.write("var serviceProtocol = environment.ServiceProtocol;");
  }

  /**
   * Renders the per-service client config: a sealed subclass of the runtime's {@code
   * SmithyClientConfig}. It inherits the common knobs today; service-specific options (e.g.
   * endpoint client-context params) can be added here later without changing the client's
   * constructor surface. The copy constructor backs the client's copy-at-construction semantics.
   */
  private void writeConfigClass(String typeName) {
    writer.pushState();
    try {
      writer.putContext("config", typeName + "Config");
      writer.putContext("baseConfig", RuntimeTypes.SMITHY_CLIENT_CONFIG);
      writer.putContext("defaults", writer.consumer(this::writeConfigDefaults));
      writer.write(
          """
          public sealed class ${config:L} : ${baseConfig:T}
          {
              public ${config:L}()
              {
                  ${defaults:C|}
              }

              public ${config:L}(${config:L} source) : base(source) { }
          }
          """);
    } finally {
      writer.popState();
    }
  }

  private void writeConfigDefaults(CSharpWriter writer) {
    if (service.getId().equals(GLACIER_SERVICE)) {
      writer.write("Interceptors.Add(new $T());", RuntimeTypes.GLACIER_INTERCEPTOR);
    }
  }

  /**
   * Renders every auth scheme that can be effective for an operation in the service. Service-level
   * schemes are followed by schemes introduced by operation-level {@code @auth} overrides, with
   * duplicates removed while preserving their first occurrence. An empty result renders as an empty
   * array.
   */
  private String modeledAuthSchemesLiteral() {
    var serviceIndex = ServiceIndex.of(context.model());
    Map<String, Boolean> uniqueIds = new LinkedHashMap<>();
    serviceIndex
        .getEffectiveAuthSchemes(service)
        .keySet()
        .forEach(id -> uniqueIds.put(id.toString(), Boolean.TRUE));
    TopDownIndex.of(context.model()).getContainedOperations(service).stream()
        .sorted(Comparator.comparing(operation -> operation.getId().toString()))
        .forEach(
            operation ->
                serviceIndex
                    .getEffectiveAuthSchemes(service, operation)
                    .keySet()
                    .forEach(id -> uniqueIds.put(id.toString(), Boolean.TRUE)));
    List<String> ids = uniqueIds.keySet().stream().toList();
    if (ids.isEmpty()) {
      return writer.typeName(RuntimeTypes.ARRAY) + ".Empty<string>()";
    }
    return "new string[] { "
        + ids.stream().map(CSharpNaming::formatString).collect(Collectors.joining(", "))
        + " }";
  }

  /**
   * In a constructor body, binds the idempotency-token provider field when the service needs it.
   */
  private void writeIdempotencyAssignment(boolean needsIdempotency) {
    if (needsIdempotency) {
      writer.write(
          "this.idempotencyTokenProvider = config.IdempotencyTokenProvider ??"
              + " DefaultIdempotencyToken;");
    }
  }

  private boolean operationNeedsIdempotencyToken(Model model, OperationShape op) {
    if (ShapeSupport.isUnit(op.getInputShape())) {
      return false;
    }
    StructureShape input = model.expectShape(op.getInputShape(), StructureShape.class);
    return ShapeSupport.sortedMembers(input).stream()
        .anyMatch(m -> m.hasTrait(IdempotencyTokenTrait.class) && ShapeSupport.isNullable(m));
  }

  private boolean operationCanDefaultIdempotencyToken(Model model, OperationShape op) {
    return operationNeedsIdempotencyToken(model, op);
  }

  // ---------------- operation bindings ----------------

  /**
   * Binds each operation's protocol from the local {@code serviceProtocol} in a constructor body.
   */
  private void writeOperationBindings(List<OperationShape> operations) {
    for (OperationShape op : operations) {
      if (!canBindOperation(op)) {
        continue;
      }
      String operationSchema = SchemaGenerator.operationSchemaAccessor(writer, context, op);
      writer.write(
          "this.$LBinding = new $T<$L, $L>($L.Id, $L.Id, serviceProtocol.ForClientOperation($L),"
              + " $L, $L);",
          CSharpNaming.typeName(op.getId().getName()),
          RuntimeTypes.SMITHY_OPERATION_BINDING,
          SchemaGenerator.operationShapeType(writer, context, op.getInputShape()),
          SchemaGenerator.operationShapeType(writer, context, op.getOutputShape()),
          SchemaGenerator.serviceSchemaAccessor(writer, context, service),
          operationSchema,
          operationSchema,
          operationAuthSchemesLiteral(op),
          operationHostPrefixLiteral(op));
    }
  }

  /**
   * The operation's effective auth schemes in Smithy priority order: the service's effective
   * schemes, overridden by a per-operation {@code @auth} trait. Rendered as a C# array literal of
   * shape-id strings for the operation binding; the runtime selects the configured auth scheme per
   * invocation from this list.
   */
  private String operationAuthSchemesLiteral(OperationShape op) {
    List<String> ids =
        ServiceIndex.of(context.model()).getEffectiveAuthSchemes(service, op).keySet().stream()
            .map(ShapeId::toString)
            .collect(Collectors.toList());
    if (ids.isEmpty()) {
      return writer.typeName(RuntimeTypes.ARRAY) + ".Empty<string>()";
    }
    return "new string[] { "
        + ids.stream().map(CSharpNaming::formatString).collect(Collectors.joining(", "))
        + " }";
  }

  /** Renders a typed host-prefix expander for an operation's {@code @endpoint} trait. */
  private String operationHostPrefixLiteral(OperationShape op) {
    Optional<EndpointTrait> endpoint = op.getTrait(EndpointTrait.class);
    if (endpoint.isEmpty()) {
      return "null";
    }

    StructureShape input = context.model().expectShape(op.getInputShape(), StructureShape.class);
    String labels =
        endpoint.get().getHostPrefix().getLabels().stream()
            .map(
                label -> {
                  String name = label.getContent();
                  MemberShape member = input.getMember(name).orElseThrow();
                  return ("new " + writer.typeName(RuntimeTypes.SMITHY_HOST_LABEL) + "(")
                      + CSharpNaming.formatString(name)
                      + ", input."
                      + CSharpNaming.propertyName(member.getMemberName())
                      + ")";
                })
            .collect(Collectors.joining(", "));
    String arguments =
        labels.isEmpty()
            ? CSharpNaming.formatString(endpoint.get().getHostPrefix().toString())
            : CSharpNaming.formatString(endpoint.get().getHostPrefix().toString()) + ", " + labels;
    return ("static input => " + writer.typeName(RuntimeTypes.SMITHY_HOST_PREFIX) + ".Expand(")
        + arguments
        + ")";
  }

  // ---------------- per-operation method ----------------

  private void writeOperationMethod(Model model, OperationShape op) {
    writer.writeXmlDocs(op, operationParameterDocs(model, op));
    if (!canBindOperation(op)) {
      writer.write("public $L", operationSignature(writer, context, op));
      writer.openBlock(
          "{",
          "}",
          () ->
              writer.write(
                  "throw new $T(\"Event-stream operations are not supported by the declared service"
                      + " protocols.\");",
                  RuntimeTypes.NOT_SUPPORTED_EXCEPTION));
      writer.write("");
      return;
    }

    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputArg = hasInput ? "input" : (writer.typeName(RuntimeTypes.SMITHY_UNIT) + ".Value");

    writer.write(
        hasOutput ? "public $L" : "public async $L", operationSignature(writer, context, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("$T.ThrowIfNull(input);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
            writeIdempotencyTokenDefaults(
                model.expectShape(op.getInputShape(), StructureShape.class));
          }

          if (hasOutput) {
            writer.write(
                "return runtime.InvokeAsync($LBinding, $L, cancellationToken);", opName, inputArg);
          } else {
            writer.write(
                "await runtime.InvokeAsync($LBinding, $L,"
                    + " cancellationToken).ConfigureAwait(false);",
                opName,
                inputArg);
            writer.write("return;");
          }
        });
    writer.write("");
  }

  private void writeIdempotencyTokenDefaults(StructureShape input) {
    List<MemberShape> idempotencyMembers =
        ShapeSupport.sortedMembers(input).stream()
            .filter(m -> m.hasTrait(IdempotencyTokenTrait.class))
            .filter(ShapeSupport::isNullable)
            .collect(Collectors.toList());
    if (idempotencyMembers.isEmpty()) {
      return;
    }

    writer.write("input = input with");
    writer.openBlock(
        "{",
        "};",
        () -> {
          for (MemberShape member : idempotencyMembers) {
            String prop = CSharpNaming.propertyName(member.getMemberName());
            writer.write("$L = input.$L ?? this.idempotencyTokenProvider(),", prop, prop);
          }
        });
  }

  private static Optional<String> paginatorItemElementType(
      CSharpWriter writer, GenerationContext context, PaginationInfo info) {
    List<MemberShape> path = info.getItemsMemberPath();
    if (path.isEmpty()) {
      return Optional.empty();
    }
    var target = context.model().expectShape(path.get(path.size() - 1).getTarget());
    if (!target.isListShape()) {
      return Optional.empty();
    }
    var element = context.model().expectShape(target.asListShape().get().getMember().getTarget());
    return Optional.of(writer.typeName(context.symbolProvider().toSymbol(element)));
  }

  /**
   * Renders the paginator methods for one {@code @paginated} operation. Pages repeat the unary call
   * while the response carries a continuation token, so every page flows through the normal client
   * lifecycle (auth, retries, endpoint resolution, telemetry).
   */
  private void writePaginatorMethods(OperationShape op, PaginationInfo info) {
    String opName = CSharpNaming.typeName(op.getId().getName());
    String tokenProperty = CSharpNaming.propertyName(info.getInputTokenMember().getMemberName());
    String outputTokenExpr = memberPathExpr("output", info.getOutputTokenMemberPath());

    writer.writeXmlDocs(op, operationParameterDocs(context.model(), op));
    writer.write(
        "public async $L",
        withEnumeratorCancellation(writer, paginatorPagesSignature(writer, context, op)));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("$T.ThrowIfNull(input);", RuntimeTypes.ARGUMENT_NULL_EXCEPTION);
          writer.write(
              "var output = await $LAsync(input, cancellationToken).ConfigureAwait(false);",
              opName);
          writer.write("yield return output;");
          writer.write("var token = $L;", outputTokenExpr);
          writer.write("while (token is not null)");
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("input = input with { $L = token };", tokenProperty);
                writer.write(
                    "output = await $LAsync(input, cancellationToken).ConfigureAwait(false);",
                    opName);
                writer.write("yield return output;");
                writer.write("token = $L;", outputTokenExpr);
              });
        });
    writer.write("");

    paginatorItemsSignature(writer, context, info)
        .ifPresent(
            signature -> {
              String itemsExpr = memberPathExpr("page", info.getItemsMemberPath());
              writer.writeXmlDocs(op, operationParameterDocs(context.model(), op));
              writer.write("public async $L", withEnumeratorCancellation(writer, signature));
              writer.openBlock(
                  "{",
                  "}",
                  () -> {
                    writer.write(
                        "await foreach (var page in $LPagesAsync(input,"
                            + " cancellationToken).ConfigureAwait(false))",
                        opName);
                    writer.openBlock(
                        "{",
                        "}",
                        () -> {
                          writer.write("var items = $L;", itemsExpr);
                          writer.write("if (items is null)");
                          writer.openBlock("{", "}", () -> writer.write("continue;"));
                          writer.write("foreach (var item in items.Values)");
                          writer.openBlock("{", "}", () -> writer.write("yield return item;"));
                        });
                  });
              writer.write("");
            });
  }

  private Map<String, String> operationParameterDocs(Model model, OperationShape op) {
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

  private static boolean isEventStreamOperation(Model model, OperationShape op) {
    return isInputStreaming(model, op) || isOutputStreaming(model, op);
  }

  private boolean canBindOperation(OperationShape op) {
    if (!isEventStreamOperation(context.model(), op)) {
      return true;
    }
    return ProtocolSupport.declaredKinds(service).stream()
        .anyMatch(ProtocolSupport::supportsEventStreams);
  }

  private static boolean isInputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getInputShape());
  }

  private static boolean isOutputStreaming(Model model, OperationShape op) {
    return ShapeSupport.isEventStreamShape(model, op.getOutputShape());
  }
}

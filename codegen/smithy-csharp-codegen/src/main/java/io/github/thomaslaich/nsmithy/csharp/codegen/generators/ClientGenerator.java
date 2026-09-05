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

    writer.addImport(RuntimeTypes.NSMITHY_CORE);

    String typeName = CSharpNaming.typeName(service.getId().getName()) + "Client";
    String interfaceName = "I" + typeName;

    // Interface — IDisposable so a client that owns its HttpClient is released via `using` or DI.
    writer.writeXmlDocs(service);
    writer.write("public interface $L : System.IDisposable", interfaceName);
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

    writer.addImport(RuntimeTypes.NSMITHY_CLIENT);
    writer.addImport(RuntimeTypes.NSMITHY_HTTP);
    writer.addImport(RuntimeTypes.NSMITHY_CORE_SERDE);
    // The generated client only names the primary protocol (the default constructor argument);
    // callers selecting another declared protocol reference its namespace from their own code.
    writer.addImport(ProtocolSupport.runtimeProtocolNamespace(kinds.get(0)));

    writeClient(model, operations, typeName, interfaceName, kinds);
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
    String primaryProtocol = ProtocolSupport.protocolType(kinds.get(0));
    String modeledHttpVersionPreference =
        httpVersionPreferenceLiteral(
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
            writer.write("private readonly SmithyClientRuntime runtime;");
            // The environment owns only resources created by the runtime factory.
            writer.write("private readonly SmithyHttpClientEnvironment environment;");
          }
          if (needsIdempotency) {
            writer.write("private readonly System.Func<string> idempotencyTokenProvider;");
          }
          // The protocol is bound at construction; per-operation protocols are built once from it.
          for (OperationShape op : operations) {
            if (!canBindOperation(op)) {
              continue;
            }
            writer.write(
                "private readonly SmithyOperationBinding<$L, $L> $LBinding;",
                SchemaGenerator.operationShapeType(writer, context, op.getInputShape()),
                SchemaGenerator.operationShapeType(writer, context, op.getOutputShape()),
                CSharpNaming.typeName(op.getId().getName()));
          }
          writer.write("");

          // Constructor: endpoint convenience overload. The endpoint argument wins over any
          // Endpoint already present on config; construction then flows through the config
          // constructor so there is only one implementation path.
          writer.write(
              "public $L(System.Uri endpoint, $LConfig? config = null) :"
                  + " this(WithEndpoint(endpoint, config))",
              typeName,
              typeName);
          writer.openBlock("{", "}", () -> {});
          writer.write("");

          // Canonical config implementation for the endpoint constructor. This stays private so
          // the public surface has one direct-construction path (`endpoint, config?`) while config
          // remains the internal model.
          writer.write("private $L($LConfig config)", typeName, typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(config);");
                if (needsRuntime) {
                  writeEnvironmentCreation(
                      serviceSchema, primaryProtocol, modeledHttpVersionPreference, "null");
                }
                writeIdempotencyAssignment(needsIdempotency);
                if (needsRuntime) {
                  writeSafeOperationBindings(operations);
                }
              });
          writer.write("");

          // Constructor: bring your own HttpClient (e.g. from IHttpClientFactory /
          // AddHttpClient<I,T>). The endpoint comes from Config.Endpoint, falling back to the
          // HttpClient's BaseAddress.
          writer.write(
              "public $L(System.Net.Http.HttpClient httpClient, $LConfig? config = null)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(httpClient);");
                if (needsRuntime) {
                  writer.write("config ??= new $LConfig();", typeName);
                  writeEnvironmentCreation(
                      serviceSchema, primaryProtocol, modeledHttpVersionPreference, "httpClient");
                }
                writeIdempotencyAssignment(needsIdempotency);
                if (needsRuntime) {
                  writeSafeOperationBindings(operations);
                }
              });
          writer.write("");

          if (needsRuntime) {
            // Constructor: bring your own runtime (custom transport/interceptors, DI, testing). The
            // runtime already owns the transport/interceptor pipeline, so config.AuthSchemes and
            // config.Interceptors do not apply here; only Protocol and IdempotencyTokenProvider are
            // read.
            writer.write(
                "public $L(SmithyClientRuntime runtime, $LConfig? config = null)",
                typeName,
                typeName);
            writer.openBlock(
                "{",
                "}",
                () -> {
                  writer.write("config ??= new $LConfig();", typeName);
                  writer.write(
                      "this.environment = SmithyHttpClientEnvironment.FromRuntime($L, runtime,"
                          + " config, static () => new $L());",
                      serviceSchema,
                      primaryProtocol);
                  writer.write("this.runtime = environment.Runtime;");
                  writer.write("var serviceProtocol = environment.ServiceProtocol;");
                  writeIdempotencyAssignment(needsIdempotency);
                  writeSafeOperationBindings(operations);
                });
            writer.write("");
          }

          // Copies the caller's config before setting the endpoint, so constructing a client
          // never mutates a config instance the caller may share with other clients.
          writer.write(
              "private static $LConfig WithEndpoint(System.Uri endpoint, $LConfig? config)",
              typeName,
              typeName);
          writer.openBlock(
              "{",
              "}",
              () -> {
                writer.write("System.ArgumentNullException.ThrowIfNull(endpoint);");
                writer.write(
                    "var copy = config is null ? new $LConfig() : new $LConfig(config);",
                    typeName,
                    typeName);
                writer.write("copy.Endpoint = endpoint;");
                writer.write("return copy;");
              });
          writer.write("");

          // Every auth scheme that can be effective for a service operation. This includes schemes
          // introduced solely by an operation-level @auth override, so constructor validation does
          // not reject a client configured specifically for such an operation.
          writer.write(
              "private static readonly System.Collections.Generic.IReadOnlyList<string>"
                  + " ModeledAuthSchemes = $L;",
              modeledAuthSchemesLiteral());
          writer.write("");

          if (needsIdempotency) {
            writer.write("");
            writer.write(
                "private static string DefaultIdempotencyToken() =>"
                    + " System.Guid.NewGuid().ToString();");
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
        "this.environment = SmithyHttpClientEnvironment.Create($L, config, static () => new $L(),"
            + " ModeledAuthSchemes, $L, $L);",
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
    writer.write("public sealed class $LConfig : SmithyClientConfig", typeName);
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (service.getId().equals(GLACIER_SERVICE)) {
            writer.write("public $LConfig()", typeName);
            writer.openBlock(
                "{",
                "}",
                () -> writer.write("Interceptors.Add(new NSmithy.Aws.GlacierInterceptor());"));
          } else {
            writer.write("public $LConfig() { }", typeName);
          }
          writer.write("");
          writer.write("public $LConfig($LConfig source) : base(source) { }", typeName, typeName);
        });
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
      return "System.Array.Empty<string>()";
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
          "this.$LBinding = new SmithyOperationBinding<$L, $L>($L.Id, $L.Id,"
              + " serviceProtocol.ForClientOperation($L), $L, $L);",
          CSharpNaming.typeName(op.getId().getName()),
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
      return "System.Array.Empty<string>()";
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
                  return "new SmithyHostLabel("
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
    return "static input => SmithyHostPrefix.Expand(" + arguments + ")";
  }

  static String httpVersionPreferenceLiteral(
      Optional<ProtocolSupport.HttpVersionPreference> preference) {
    if (preference.isEmpty()) {
      return "null";
    }
    String version =
        switch (preference.get().alpnId()) {
          case "h3" -> "System.Net.HttpVersion.Version30";
          case "h2" -> "System.Net.HttpVersion.Version20";
          case "http/1.1" -> "System.Net.HttpVersion.Version11";
          default -> throw new IllegalArgumentException(preference.get().alpnId());
        };
    return "new SmithyHttpVersionPreference("
        + version
        + ", allowDowngrade: "
        + preference.get().allowDowngrade()
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
                  "throw new System.NotSupportedException(\"Event-stream operations are not"
                      + " supported by the declared service protocols.\");"));
      writer.write("");
      return;
    }

    boolean hasInput = !ShapeSupport.isUnit(op.getInputShape());
    boolean hasOutput = !ShapeSupport.isUnit(op.getOutputShape());
    String opName = CSharpNaming.typeName(op.getId().getName());
    String inputArg = hasInput ? "input" : "SmithyUnit.Value";

    writer.write(
        hasOutput ? "public $L" : "public async $L", operationSignature(writer, context, op));
    writer.openBlock(
        "{",
        "}",
        () -> {
          if (hasInput) {
            writer.write("System.ArgumentNullException.ThrowIfNull(input);");
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
    return "System.Collections.Generic.IAsyncEnumerable<"
        + outputType
        + "> "
        + CSharpNaming.typeName(op.getId().getName())
        + "PagesAsync("
        + inputType
        + " input, System.Threading.CancellationToken cancellationToken = default)";
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
                "System.Collections.Generic.IAsyncEnumerable<"
                    + elementType
                    + "> "
                    + CSharpNaming.typeName(info.getOperation().getId().getName())
                    + "ItemsAsync("
                    + writer.typeName(
                        context
                            .symbolProvider()
                            .toSymbol(
                                context.model().expectShape(info.getOperation().getInputShape())))
                    + " input, System.Threading.CancellationToken cancellationToken = default)");
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

  /** Renders a null-safe property access chain for a member path, e.g. {@code output.A?.B}. */
  static String memberPathExpr(String root, List<MemberShape> path) {
    StringBuilder expr = new StringBuilder(root);
    for (int i = 0; i < path.size(); i++) {
      expr.append(i == 0 ? "." : "?.")
          .append(CSharpNaming.propertyName(path.get(i).getMemberName()));
    }
    return expr.toString();
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
        withEnumeratorCancellation(paginatorPagesSignature(writer, context, op)));
    writer.openBlock(
        "{",
        "}",
        () -> {
          writer.write("System.ArgumentNullException.ThrowIfNull(input);");
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
              writer.write("public async $L", withEnumeratorCancellation(signature));
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
  static String withEnumeratorCancellation(String signature) {
    return signature.replace(
        "System.Threading.CancellationToken cancellationToken",
        "[System.Runtime.CompilerServices.EnumeratorCancellation]"
            + " System.Threading.CancellationToken cancellationToken");
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

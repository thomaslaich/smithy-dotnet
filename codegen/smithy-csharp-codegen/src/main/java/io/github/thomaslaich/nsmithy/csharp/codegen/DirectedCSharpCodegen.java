package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.OwnedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointResolverGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.endpoints.EndpointRules;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ClientDependencyInjectionGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ClientGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ErrorGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.FakeClientGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.FakeHandlerGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.IntEnumGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.OperationHandlerGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.OperationSchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.SchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ServerGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ServiceSchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.StringEnumGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.StructureGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.UnionGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpServiceGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.waiters.WaiterMethods;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import java.util.Optional;
import java.util.Set;
import java.util.function.Consumer;
import software.amazon.smithy.codegen.core.CodegenException;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.codegen.core.directed.CreateContextDirective;
import software.amazon.smithy.codegen.core.directed.CreateSymbolProviderDirective;
import software.amazon.smithy.codegen.core.directed.CustomizeDirective;
import software.amazon.smithy.codegen.core.directed.DirectedCodegen;
import software.amazon.smithy.codegen.core.directed.GenerateEnumDirective;
import software.amazon.smithy.codegen.core.directed.GenerateErrorDirective;
import software.amazon.smithy.codegen.core.directed.GenerateIntEnumDirective;
import software.amazon.smithy.codegen.core.directed.GenerateListDirective;
import software.amazon.smithy.codegen.core.directed.GenerateMapDirective;
import software.amazon.smithy.codegen.core.directed.GenerateOperationDirective;
import software.amazon.smithy.codegen.core.directed.GenerateResourceDirective;
import software.amazon.smithy.codegen.core.directed.GenerateServiceDirective;
import software.amazon.smithy.codegen.core.directed.GenerateStructureDirective;
import software.amazon.smithy.codegen.core.directed.GenerateUnionDirective;
import software.amazon.smithy.model.knowledge.TopDownIndex;
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.ServiceShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
final class DirectedCSharpCodegen
    implements DirectedCodegen<GenerationContext, CSharpSettings, CSharpIntegration> {

  /** What this run generated, written as the shape manifest once generation is done. */
  private final ShapeManifest shapeManifest = new ShapeManifest(ShapeManifest.CSHARP_GENERATOR);

  /** The shapes defined in this project's own model files, read on first use. */
  private Set<ShapeId> ownedShapes;

  @Override
  public SymbolProvider createSymbolProvider(
      CreateSymbolProviderDirective<CSharpSettings> directive) {
    return new CSharpSymbolProvider(directive.model(), directive.settings());
  }

  @Override
  public GenerationContext createContext(
      CreateContextDirective<CSharpSettings, CSharpIntegration> directive) {
    return GenerationContext.builder()
        .model(directive.model())
        .settings(directive.settings())
        .symbolProvider(directive.symbolProvider())
        .fileManifest(directive.fileManifest())
        .writerDelegator(
            new CSharpDelegator(
                directive.fileManifest(),
                directive.symbolProvider(),
                directive.model(),
                directive.settings()))
        .integrations(directive.integrations().stream().toList())
        .build();
  }

  @Override
  public void generateService(
      GenerateServiceDirective<GenerationContext, CSharpSettings> directive) {
    GenerationContext ctx = directive.context();
    ServiceShape service = directive.shape();
    // A project generates only the service it names. Other services in its model, such as one in
    // a contracts project's own model files, generate nothing.
    if (!ctx.settings().service().map(service.getId()::equals).orElse(false)
        || isProvided(ctx, service)) {
      return;
    }
    List<CSharpServiceGenerator> serviceGenerators =
        ctx.integrations().stream()
            .map(integration -> integration.serviceGenerator(ctx, service))
            .flatMap(Optional::stream)
            .toList();
    if (serviceGenerators.size() > 1) {
      throw new CodegenException(
          "More than one C# integration provides a service generator for " + service.getId());
    }
    if (!serviceGenerators.isEmpty()) {
      serviceGenerators.get(0).generate(ctx, service);
      return;
    }

    String csNamespace = ctx.settings().csharpNamespace(directive.shape().getId().getNamespace());
    String typeName = CSharpNaming.typeName(directive.shape().getId().getName());
    String dir = csNamespace.replace('.', '/');

    // The service schema is consumed by both client and server, so it has no ".Client"/".Server"
    // suffix and is always compiled.
    ctx.writerDelegator()
        .useFileWriter(
            dir + "/" + typeName + ".Schema.g.cs",
            SchemaGenerator.schemaNamespace(csNamespace),
            writer -> new ServiceSchemaGenerator(ctx, writer, directive.shape()).run());
    shapeManifest.add(
        service.getId(), ShapeManifest.SCHEMA, SchemaGenerator.schemaClassSymbol(ctx, service));

    // Service-level files use a dotted ".Client"/".Server" suffix so the MSBuild
    // include/exclude globs (*.Client.g.cs / *.Server.g.cs) can distinguish them from
    // shape files for operations whose names happen to end in "Client" or "Server". Each half is
    // gated by settings so a client- or server-only project never writes the half it discards —
    // the MSBuild compile-time exclusion is then belt-and-suspenders.
    if (ctx.settings().generateClient()) {
      Optional<EndpointRules> endpointRules = EndpointRules.of(ctx, directive.shape());
      ctx.writerDelegator()
          .useFileWriter(
              dir + "/" + typeName + ".Client.g.cs",
              csNamespace,
              writer -> new ClientGenerator(ctx, writer, directive.shape(), endpointRules).run());
      endpointRules.ifPresent(rules ->
          ctx.writerDelegator()
              .useFileWriter(
                  dir + "/" + typeName + ".Endpoints.Client.g.cs",
                  csNamespace,
                  writer -> new EndpointResolverGenerator(ctx, writer, rules).run()));
      if (WaiterMethods.hasWaiters(ctx, directive.shape())) {
        ctx.writerDelegator()
            .useFileWriter(
                dir + "/" + typeName + ".Waiters.Client.g.cs",
                csNamespace,
                writer -> new WaiterGenerator(ctx, writer, directive.shape()).run());
      }
    }

    if (ctx.settings().generateServer()) {
      ctx.writerDelegator()
          .useFileWriter(
              dir + "/" + typeName + ".Server.g.cs",
              csNamespace,
              writer -> new ServerGenerator(ctx, writer, directive.shape()).run());
    }

    // Opt-in fakes, one per generated surface: the fake handler implements the ".Server" half's
    // handler interfaces and the fake client the ".Client" half's client interface, so each fake
    // exists only alongside its surface. The ".Fakes.Server"/".Fakes.Client" suffixes keep the
    // files inside the MSBuild *.Server.g.cs / *.Client.g.cs include/exclude globs.
    if (ctx.settings().generateFakes()) {
      if (!ctx.settings().generateServer() && !ctx.settings().generateClient()) {
        throw new CodegenException(
            "generateFakes requires generateServer or generateClient: the fakes implement the"
                + " generated server handler and client interfaces.");
      }
      if (ctx.settings().generateServer()) {
        ctx.writerDelegator()
            .useFileWriter(
                dir + "/" + typeName + ".Fakes.Server.g.cs",
                csNamespace,
                writer -> new FakeHandlerGenerator(ctx, writer, directive.shape()).run());
      }
      if (ctx.settings().generateClient()) {
        ctx.writerDelegator()
            .useFileWriter(
                dir + "/" + typeName + ".Fakes.Client.g.cs",
                csNamespace,
                writer -> new FakeClientGenerator(ctx, writer, directive.shape()).run());
      }
    }

    // Opt-in IHttpClientFactory registration. Generated only when requested
    // (generateDependencyInjection), because the file pulls in Microsoft.Extensions.Http — a
    // dependency plain clients must not be forced to carry. Gating generation (rather than
    // compilation) means the file simply does not exist unless asked for.
    if (ctx.settings().generateDependencyInjection()) {
      ctx.writerDelegator()
          .useFileWriter(
              dir + "/" + typeName + ".DependencyInjection.g.cs",
              csNamespace,
              writer ->
                  new ClientDependencyInjectionGenerator(ctx, writer, directive.shape()).run());
    }
  }

  @Override
  public void generateStructure(
      GenerateStructureDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    // smithy.api#Unit maps to the runtime SmithyUnit type, so no record is generated for it.
    if (ShapeSupport.isUnit(directive.shape().getId())) {
      return;
    }
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new StructureGenerator(directive.context(), writer, directive.shape()).run());
    recordType(directive.context(), directive.shape());
    writeSchema(
        directive.context(),
        directive.shape(),
        writer ->
            SchemaGenerator.writeStructureSchema(
                writer,
                directive.context(),
                directive.shape(),
                List.copyOf(directive.shape().members())));
  }

  @Override
  public void generateError(GenerateErrorDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    if (ShapeSupport.isValidationException(directive.shape().getId())) {
      return;
    }

    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new ErrorGenerator(directive.context(), writer, directive.shape()).run());
    recordType(directive.context(), directive.shape());
    writeSchema(
        directive.context(),
        directive.shape(),
        writer ->
            SchemaGenerator.writeStructureSchema(
                writer,
                directive.context(),
                directive.shape(),
                ShapeSupport.sortedMembers(directive.shape())));
  }

  @Override
  public void generateUnion(GenerateUnionDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new UnionGenerator(directive.context(), writer, directive.shape()).run());
    recordType(directive.context(), directive.shape());
    writeSchema(
        directive.context(),
        directive.shape(),
        writer ->
            SchemaGenerator.writeUnionSchema(
                writer,
                directive.context(),
                directive.shape(),
                ShapeSupport.sortedMembers(directive.shape())));
  }

  @Override
  public void generateList(GenerateListDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    writeSchema(
        directive.context(),
        directive.shape(),
        writer -> SchemaGenerator.writeListSchema(writer, directive.context(), directive.shape()));
  }

  @Override
  public void generateMap(GenerateMapDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    writeSchema(
        directive.context(),
        directive.shape(),
        writer -> SchemaGenerator.writeMapSchema(writer, directive.context(), directive.shape()));
  }

  @Override
  public void generateEnumShape(
      GenerateEnumDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    // A string carrying the deprecated @enum trait reaches this directive too, but it stays a
    // `string` in the generated API: the symbol provider maps every string shape to `string`, and
    // an @enum entry is not even required to carry the name a C# type would need. Its value set
    // still reaches the runtime — the trait is inlined onto every member targeting the shape — so a
    // server rejects a value outside it just as it does for an enum shape.
    if (directive.shape().asEnumShape().isEmpty()) {
      return;
    }

    EnumShape enumShape = directive.shape().asEnumShape().get();
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(enumShape, writer -> new StringEnumGenerator(writer, enumShape).run());
    recordType(directive.context(), enumShape);
    writeSchema(
        directive.context(),
        enumShape,
        writer -> SchemaGenerator.writeSimpleSchema(writer, enumShape));
  }

  @Override
  public void generateIntEnumShape(
      GenerateIntEnumDirective<GenerationContext, CSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new IntEnumGenerator(writer, directive.shape().asIntEnumShape().get()).run());
    recordType(directive.context(), directive.shape());
    writeSchema(
        directive.context(),
        directive.shape(),
        writer ->
            SchemaGenerator.writeIntEnumSchema(
                writer, directive.shape().asIntEnumShape().orElseThrow()));
  }

  @Override
  public void generateOperation(GenerateOperationDirective<GenerationContext, CSharpSettings> d) {
    if (isProvided(d.context(), d.shape())) {
      return;
    }
    // An operation has no C# type of its own, only a schema and its handler interface.
    writeSchema(
        d.context(),
        d.shape(),
        writer -> new OperationSchemaGenerator(d.context(), writer, d.shape()).run());
    if (generatesHandler(d.context(), d.shape())) {
      Symbol handler = OperationHandlerGenerator.handlerSymbol(d.context().settings(), d.shape());
      d.context()
          .writerDelegator()
          .useFileWriter(
              handler.getDefinitionFile(),
              handler.getNamespace(),
              writer -> new OperationHandlerGenerator(d.context(), writer, d.shape()).run());
      shapeManifest.add(d.shape().getId(), ShapeManifest.HANDLER, handler);
    }
  }

  /**
   * Whether an operation gets a handler interface. A project that owns the operation always
   * generates it, since a server built on that project cannot; any other operation gets one when
   * the server is generated. The exception is an operation of a service whose surface an
   * integration's service generator replaces.
   */
  private boolean generatesHandler(GenerationContext ctx, OperationShape op) {
    if (ownedShapes == null) {
      ownedShapes = OwnedShapes.of(ctx.model(), ctx.settings().ownedSources());
    }
    if (!ownedShapes.contains(op.getId()) && !ctx.settings().generateServer()) {
      return false;
    }
    Optional<ServiceShape> service =
        ctx.settings().service().map(id -> ctx.model().expectShape(id, ServiceShape.class));
    if (service.isEmpty()
        || !TopDownIndex.of(ctx.model()).getContainedOperations(service.get()).contains(op)) {
      return true;
    }
    return ctx.integrations().stream()
        .noneMatch(integration -> integration.serviceGenerator(ctx, service.get()).isPresent());
  }

  /** Whether another project generated {@code shape}, so this one refers to it instead. */
  private static boolean isProvided(GenerationContext ctx, Shape shape) {
    return ctx.settings().providedShapes().contains(shape.getId());
  }

  /** Records the C# type a shape is generated as in the shape manifest. */
  private void recordType(GenerationContext ctx, Shape shape) {
    shapeManifest.add(shape.getId(), ShapeManifest.TYPE, ctx.symbolProvider().toSymbol(shape));
  }

  /**
   * Writes a shape's schema to a file beside the shape's type: {@code Person.g.cs} holds the type a
   * consumer uses, {@code Person.Schema.g.cs} the schema the runtime serializes it with.
   */
  private void writeSchema(GenerationContext context, Shape shape, Consumer<CSharpWriter> body) {
    shapeManifest.add(
        shape.getId(), ShapeManifest.SCHEMA, SchemaGenerator.schemaClassSymbol(context, shape));
    Symbol symbol = SchemaGenerator.schemaOwner(context, shape);
    String file = symbol.getDefinitionFile().replaceFirst("\\.g\\.cs$", ".Schema.g.cs");
    context
        .writerDelegator()
        .useFileWriter(
            file,
            SchemaGenerator.schemaNamespace(symbol.getNamespace()),
            writer -> {
              writer.reserveMemberNames(shape);
              body.accept(writer);
            });
  }

  @Override
  public void generateResource(GenerateResourceDirective<GenerationContext, CSharpSettings> d) {
    /* not yet supported */
  }

  @Override
  public void customizeBeforeShapeGeneration(
      CustomizeDirective<GenerationContext, CSharpSettings> d) {}

  @Override
  public void customizeBeforeIntegrations(
      CustomizeDirective<GenerationContext, CSharpSettings> d) {}

  @Override
  public void customizeAfterIntegrations(CustomizeDirective<GenerationContext, CSharpSettings> d) {
    shapeManifest.write(d.context().fileManifest(), d.settings().providedShapes());
  }
}

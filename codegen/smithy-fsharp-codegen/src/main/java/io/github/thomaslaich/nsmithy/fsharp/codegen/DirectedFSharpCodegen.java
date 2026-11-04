package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.OwnedShapes;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeManifest;
import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
import java.util.Set;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.codegen.core.WriterDelegator;
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
import software.amazon.smithy.codegen.core.directed.GenerateServiceDirective;
import software.amazon.smithy.codegen.core.directed.GenerateStructureDirective;
import software.amazon.smithy.codegen.core.directed.GenerateUnionDirective;
import software.amazon.smithy.model.shapes.OperationShape;
import software.amazon.smithy.model.shapes.Shape;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * Generates the F# type and schema of every shape, each operation's schema and handler interface,
 * and the service's client and server. A shape another project generated (a provided shape) is
 * skipped; every generated shape is recorded in the shape manifest. See designs/shape-ownership.md.
 */
@SmithyUnstableApi
final class DirectedFSharpCodegen
    implements DirectedCodegen<FSharpContext, FSharpSettings, FSharpIntegration> {

  private final ShapeManifest shapeManifest = new ShapeManifest(ShapeManifest.FSHARP_GENERATOR);
  private Set<ShapeId> ownedShapes;

  @Override
  public SymbolProvider createSymbolProvider(
      CreateSymbolProviderDirective<FSharpSettings> directive) {
    return new FSharpSymbolProvider(directive.model(), directive.settings());
  }

  @Override
  public FSharpContext createContext(
      CreateContextDirective<FSharpSettings, FSharpIntegration> directive) {
    return new FSharpContext(
        directive.model(),
        directive.settings(),
        // The directed symbol provider is wrapped in a cache; generators also need the F#-specific
        // queries, which are cheap enough to answer uncached.
        new FSharpSymbolProvider(directive.model(), directive.settings()),
        directive.fileManifest(),
        new WriterDelegator<>(
            directive.fileManifest(),
            directive.symbolProvider(),
            (file, namespace) -> new FSharpWriter()),
        directive.integrations().stream().toList(),
        new FSharpFiles());
  }

  @Override
  public void generateService(GenerateServiceDirective<FSharpContext, FSharpSettings> directive) {
    new SchemaGenerator(directive.context()).service(directive.shape());
    recordSchema(directive.context(), directive.shape());
    if (directive.settings().generateClient()) {
      new ClientGenerator(directive.context(), directive.shape()).run();
    }
    if (directive.settings().generateServer()) {
      new ServerGenerator(directive.context(), directive.shape()).run();
    }
  }

  @Override
  public void generateOperation(
      GenerateOperationDirective<FSharpContext, FSharpSettings> directive) {
    FSharpContext context = directive.context();
    OperationShape operation = directive.shape();
    if (isProvided(context, operation)) {
      return;
    }
    new SchemaGenerator(context).operation(operation);
    recordSchema(context, operation);
    if (generatesHandler(context, operation)) {
      new OperationHandlerGenerator(context).run(operation);
      shapeManifest.add(
          operation.getId(),
          ShapeManifest.HANDLER,
          context.symbolProvider().handlerSymbol(operation));
    }
  }

  /**
   * Whether an operation gets a handler interface. A project that owns the operation always
   * generates it, since a server built on that project cannot; any other operation gets one when
   * the server is generated.
   */
  private boolean generatesHandler(FSharpContext context, OperationShape operation) {
    if (ownedShapes == null) {
      ownedShapes = OwnedShapes.of(context.model(), context.settings().ownedSources());
    }
    return ownedShapes.contains(operation.getId()) || context.settings().generateServer();
  }

  @Override
  public void generateStructure(
      GenerateStructureDirective<FSharpContext, FSharpSettings> directive) {
    // smithy.api#Unit binds to the runtime's SmithyUnit.
    if (ShapeSupport.isUnit(directive.shape().getId())
        || isProvided(directive.context(), directive.shape())) {
      return;
    }
    new TypeGenerator(directive.context()).structure(directive.shape());
    new SchemaGenerator(directive.context()).structure(directive.shape());
    recordType(directive.context(), directive.shape());
  }

  @Override
  public void generateError(GenerateErrorDirective<FSharpContext, FSharpSettings> directive) {
    // The server runtime throws smithy.framework#ValidationException itself, so the model binds to
    // the runtime's type and schema.
    if (ShapeSupport.isValidationException(directive.shape().getId())
        || isProvided(directive.context(), directive.shape())) {
      return;
    }
    new TypeGenerator(directive.context()).error(directive.shape());
    new SchemaGenerator(directive.context()).structure(directive.shape());
    recordType(directive.context(), directive.shape());
  }

  @Override
  public void generateUnion(GenerateUnionDirective<FSharpContext, FSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    new TypeGenerator(directive.context()).union(directive.shape());
    new SchemaGenerator(directive.context()).union(directive.shape());
    recordType(directive.context(), directive.shape());
  }

  @Override
  public void generateList(GenerateListDirective<FSharpContext, FSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    new SchemaGenerator(directive.context()).list(directive.shape());
    recordSchema(directive.context(), directive.shape());
  }

  @Override
  public void generateMap(GenerateMapDirective<FSharpContext, FSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    new SchemaGenerator(directive.context()).map(directive.shape());
    recordSchema(directive.context(), directive.shape());
  }

  /**
   * A string with the deprecated {@code @enum} trait reaches this directive too, but stays a {@code
   * string}; its values reach the runtime as the trait, which every member targeting it carries.
   */
  @Override
  public void generateEnumShape(GenerateEnumDirective<FSharpContext, FSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    directive
        .shape()
        .asEnumShape()
        .ifPresent(shape -> {
          new TypeGenerator(directive.context()).stringEnum(shape);
          new SchemaGenerator(directive.context()).stringEnum(shape);
          recordType(directive.context(), shape);
        });
  }

  @Override
  public void generateIntEnumShape(
      GenerateIntEnumDirective<FSharpContext, FSharpSettings> directive) {
    if (isProvided(directive.context(), directive.shape())) {
      return;
    }
    var shape = directive.shape().asIntEnumShape().orElseThrow();
    new TypeGenerator(directive.context()).intEnum(shape);
    new SchemaGenerator(directive.context()).intEnum(shape);
    recordType(directive.context(), shape);
  }

  @Override
  public void customizeAfterIntegrations(CustomizeDirective<FSharpContext, FSharpSettings> d) {
    FSharpContext context = d.context();
    context.files().write(context.fileManifest(), context.settings());
    shapeManifest.write(context.fileManifest(), context.settings().providedShapes());
  }

  /** Whether another project generated {@code shape}, so this one refers to it instead. */
  private static boolean isProvided(FSharpContext context, Shape shape) {
    return context.settings().providedShapes().contains(shape.getId());
  }

  /** Records the F# type and the schema a shape is generated as in the shape manifest. */
  private void recordType(FSharpContext context, Shape shape) {
    shapeManifest.add(shape.getId(), ShapeManifest.TYPE, context.symbolProvider().toSymbol(shape));
    recordSchema(context, shape);
  }

  /** Records the schema class a shape is generated as in the shape manifest. */
  private void recordSchema(FSharpContext context, Shape shape) {
    shapeManifest.add(
        shape.getId(), ShapeManifest.SCHEMA, context.symbolProvider().schemaSymbol(shape));
  }
}

package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ShapeSupport;
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
import software.amazon.smithy.codegen.core.directed.GenerateServiceDirective;
import software.amazon.smithy.codegen.core.directed.GenerateStructureDirective;
import software.amazon.smithy.codegen.core.directed.GenerateUnionDirective;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** Generates the F# type and schema of every shape. Services and operations generate nothing. */
@SmithyUnstableApi
final class DirectedFSharpCodegen
    implements DirectedCodegen<FSharpContext, FSharpSettings, FSharpIntegration> {

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
  public void generateService(GenerateServiceDirective<FSharpContext, FSharpSettings> directive) {}

  @Override
  public void generateStructure(
      GenerateStructureDirective<FSharpContext, FSharpSettings> directive) {
    // smithy.api#Unit binds to the runtime's SmithyUnit.
    if (ShapeSupport.isUnit(directive.shape().getId())) {
      return;
    }
    new TypeGenerator(directive.context()).structure(directive.shape());
    new SchemaGenerator(directive.context()).structure(directive.shape());
  }

  @Override
  public void generateError(GenerateErrorDirective<FSharpContext, FSharpSettings> directive) {
    // The server runtime throws smithy.framework#ValidationException itself, so the model binds to
    // the runtime's type and schema.
    if (ShapeSupport.isValidationException(directive.shape().getId())) {
      return;
    }
    new TypeGenerator(directive.context()).error(directive.shape());
    new SchemaGenerator(directive.context()).structure(directive.shape());
  }

  @Override
  public void generateUnion(GenerateUnionDirective<FSharpContext, FSharpSettings> directive) {
    new TypeGenerator(directive.context()).union(directive.shape());
    new SchemaGenerator(directive.context()).union(directive.shape());
  }

  @Override
  public void generateList(GenerateListDirective<FSharpContext, FSharpSettings> directive) {
    new SchemaGenerator(directive.context()).list(directive.shape());
  }

  @Override
  public void generateMap(GenerateMapDirective<FSharpContext, FSharpSettings> directive) {
    new SchemaGenerator(directive.context()).map(directive.shape());
  }

  /**
   * A string with the deprecated {@code @enum} trait reaches this directive too, but stays a {@code
   * string}; its values reach the runtime as the trait, which every member targeting it carries.
   */
  @Override
  public void generateEnumShape(GenerateEnumDirective<FSharpContext, FSharpSettings> directive) {
    directive
        .shape()
        .asEnumShape()
        .ifPresent(
            shape -> {
              new TypeGenerator(directive.context()).stringEnum(shape);
              new SchemaGenerator(directive.context()).stringEnum(shape);
            });
  }

  @Override
  public void generateIntEnumShape(
      GenerateIntEnumDirective<FSharpContext, FSharpSettings> directive) {
    var shape = directive.shape().asIntEnumShape().orElseThrow();
    new TypeGenerator(directive.context()).intEnum(shape);
    new SchemaGenerator(directive.context()).intEnum(shape);
  }

  @Override
  public void customizeAfterIntegrations(CustomizeDirective<FSharpContext, FSharpSettings> d) {
    FSharpContext context = d.context();
    context.files().write(context.fileManifest(), context.settings(), new SchemaGenerator(context));
  }
}

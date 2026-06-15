package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ClientGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ErrorGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.IntEnumGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ListGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.MapGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.OperationSchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ServerGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ServiceSchemaGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.StringEnumGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.StructureGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.generators.UnionGenerator;
import io.github.thomaslaich.nsmithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.nsmithy.csharp.codegen.support.ShapeSupport;
import io.github.thomaslaich.nsmithy.csharp.codegen.writer.CSharpDelegator;
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
import software.amazon.smithy.model.shapes.EnumShape;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
final class DirectedCSharpCodegen
    implements DirectedCodegen<GenerationContext, CSharpSettings, CSharpIntegration> {

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
        .writerDelegator(new CSharpDelegator(directive.fileManifest(), directive.symbolProvider()))
        .integrations(directive.integrations().stream().toList())
        .build();
  }

  @Override
  public void generateService(
      GenerateServiceDirective<GenerationContext, CSharpSettings> directive) {
    GenerationContext ctx = directive.context();
    String csNamespace = ctx.settings().csharpNamespace(directive.shape().getId().getNamespace());
    String typeName = CSharpNaming.typeName(directive.shape().getId().getName());
    String dir = csNamespace.replace('.', '/');

    // The service schema is consumed by both client and server, so it has no ".Client"/".Server"
    // suffix and is always compiled.
    ctx.writerDelegator()
        .useFileWriter(
            dir + "/" + typeName + ".Schema.g.cs",
            csNamespace,
            writer -> new ServiceSchemaGenerator(ctx, writer, directive.shape()).run());

    // Service-level files use a dotted ".Client"/".Server" suffix so the MSBuild
    // include/exclude globs (*.Client.g.cs / *.Server.g.cs) can distinguish them from
    // shape files for operations whose names happen to end in "Client" or "Server".
    ctx.writerDelegator()
        .useFileWriter(
            dir + "/" + typeName + ".Client.g.cs",
            csNamespace,
            writer -> new ClientGenerator(ctx, writer, directive.shape()).run());

    ctx.writerDelegator()
        .useFileWriter(
            dir + "/" + typeName + ".Server.g.cs",
            csNamespace,
            writer -> new ServerGenerator(ctx, writer, directive.shape()).run());
  }

  @Override
  public void generateStructure(
      GenerateStructureDirective<GenerationContext, CSharpSettings> directive) {
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
  }

  @Override
  public void generateError(GenerateErrorDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new ErrorGenerator(directive.context(), writer, directive.shape()).run());
  }

  @Override
  public void generateUnion(GenerateUnionDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new UnionGenerator(directive.context(), writer, directive.shape()).run());
  }

  @Override
  public void generateList(GenerateListDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new ListGenerator(directive.context(), writer, directive.shape()).run());
  }

  @Override
  public void generateMap(GenerateMapDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new MapGenerator(directive.context(), writer, directive.shape()).run());
  }

  @Override
  public void generateEnumShape(
      GenerateEnumDirective<GenerationContext, CSharpSettings> directive) {
    EnumShape enumShape =
        directive
            .shape()
            .asEnumShape()
            .or(() -> directive.shape().asStringShape().flatMap(EnumShape::fromStringShape))
            .orElseThrow(
                () ->
                    new IllegalStateException(
                        "Expected enum shape for " + directive.shape().getId()));
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            enumShape,
            writer -> new StringEnumGenerator(directive.context(), writer, enumShape).run());
  }

  @Override
  public void generateIntEnumShape(
      GenerateIntEnumDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer ->
                new IntEnumGenerator(
                        directive.context(), writer, directive.shape().asIntEnumShape().get())
                    .run());
  }

  @Override
  public void generateOperation(GenerateOperationDirective<GenerationContext, CSharpSettings> d) {
    d.context()
        .writerDelegator()
        .useShapeWriter(
            d.shape(),
            writer -> new OperationSchemaGenerator(d.context(), writer, d.shape()).run());
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
  public void customizeAfterIntegrations(CustomizeDirective<GenerationContext, CSharpSettings> d) {}
}

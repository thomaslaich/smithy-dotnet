package io.github.thomaslaich.nsmithy.csharp.codegen;

import io.github.thomaslaich.nsmithy.csharp.codegen.generators.ClientDependencyInjectionGenerator;
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
            writer -> new ServiceSchemaGenerator(writer, directive.shape()).run());

    // Service-level files use a dotted ".Client"/".Server" suffix so the MSBuild
    // include/exclude globs (*.Client.g.cs / *.Server.g.cs) can distinguish them from
    // shape files for operations whose names happen to end in "Client" or "Server". Each half is
    // gated by settings so a client- or server-only project never writes the half it discards —
    // the MSBuild compile-time exclusion is then belt-and-suspenders.
    if (ctx.settings().generateClient()) {
      ctx.writerDelegator()
          .useFileWriter(
              dir + "/" + typeName + ".Client.g.cs",
              csNamespace,
              writer -> new ClientGenerator(ctx, writer, directive.shape()).run());
    }

    if (ctx.settings().generateServer()) {
      ctx.writerDelegator()
          .useFileWriter(
              dir + "/" + typeName + ".Server.g.cs",
              csNamespace,
              writer -> new ServerGenerator(ctx, writer, directive.shape()).run());
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
    if (ShapeSupport.isValidationException(directive.shape().getId())) {
      return;
    }

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
  }

  @Override
  public void generateIntEnumShape(
      GenerateIntEnumDirective<GenerationContext, CSharpSettings> directive) {
    directive
        .context()
        .writerDelegator()
        .useShapeWriter(
            directive.shape(),
            writer -> new IntEnumGenerator(writer, directive.shape().asIntEnumShape().get()).run());
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

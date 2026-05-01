/*
 * CodegenContext for the C# generator. Mirrors smithy-python's
 * GenerationContext: a builder-based holder of model + settings + symbol
 * provider + writer delegator + integrations, exposed as CodegenContext so
 * the DirectedCodegen orchestration plays naturally.
 */
package io.github.thomaslaich.smithy.csharp.codegen;

import io.github.thomaslaich.smithy.csharp.codegen.integrations.CSharpIntegration;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpDelegator;
import io.github.thomaslaich.smithy.csharp.codegen.writer.CSharpWriter;
import java.util.List;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.codegen.core.CodegenContext;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.utils.SmithyBuilder;
import software.amazon.smithy.utils.SmithyUnstableApi;

@SmithyUnstableApi
public final class GenerationContext
    implements CodegenContext<CSharpSettings, CSharpWriter, CSharpIntegration> {

  private final Model model;
  private final CSharpSettings settings;
  private final SymbolProvider symbolProvider;
  private final FileManifest fileManifest;
  private final CSharpDelegator writerDelegator;
  private final List<CSharpIntegration> integrations;

  private GenerationContext(Builder b) {
    this.model = SmithyBuilder.requiredState("model", b.model);
    this.settings = SmithyBuilder.requiredState("settings", b.settings);
    this.symbolProvider = SmithyBuilder.requiredState("symbolProvider", b.symbolProvider);
    this.fileManifest = SmithyBuilder.requiredState("fileManifest", b.fileManifest);
    this.writerDelegator = SmithyBuilder.requiredState("writerDelegator", b.writerDelegator);
    this.integrations = b.integrations == null ? List.of() : List.copyOf(b.integrations);
  }

  public static Builder builder() {
    return new Builder();
  }

  @Override
  public Model model() {
    return model;
  }

  @Override
  public CSharpSettings settings() {
    return settings;
  }

  @Override
  public SymbolProvider symbolProvider() {
    return symbolProvider;
  }

  @Override
  public FileManifest fileManifest() {
    return fileManifest;
  }

  @Override
  public CSharpDelegator writerDelegator() {
    return writerDelegator;
  }

  @Override
  public List<CSharpIntegration> integrations() {
    return integrations;
  }

  public static final class Builder implements SmithyBuilder<GenerationContext> {
    private Model model;
    private CSharpSettings settings;
    private SymbolProvider symbolProvider;
    private FileManifest fileManifest;
    private CSharpDelegator writerDelegator;
    private List<CSharpIntegration> integrations;

    public Builder model(Model v) {
      this.model = v;
      return this;
    }

    public Builder settings(CSharpSettings v) {
      this.settings = v;
      return this;
    }

    public Builder symbolProvider(SymbolProvider v) {
      this.symbolProvider = v;
      return this;
    }

    public Builder fileManifest(FileManifest v) {
      this.fileManifest = v;
      return this;
    }

    public Builder writerDelegator(CSharpDelegator v) {
      this.writerDelegator = v;
      return this;
    }

    public Builder integrations(List<CSharpIntegration> v) {
      this.integrations = v;
      return this;
    }

    @Override
    public GenerationContext build() {
      return new GenerationContext(this);
    }
  }
}

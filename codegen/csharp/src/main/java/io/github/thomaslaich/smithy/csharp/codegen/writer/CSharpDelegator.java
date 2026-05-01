/*
 * WriterDelegator specialization. Mirrors PythonDelegator: a thin wrapper that
 * binds the SymbolProvider, FileManifest and CSharpWriter factory together.
 */
package io.github.thomaslaich.smithy.csharp.codegen.writer;

import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.codegen.core.SymbolProvider;
import software.amazon.smithy.codegen.core.WriterDelegator;
import software.amazon.smithy.utils.SmithyInternalApi;

@SmithyInternalApi
public final class CSharpDelegator extends WriterDelegator<CSharpWriter> {

  public CSharpDelegator(FileManifest fileManifest, SymbolProvider symbolProvider) {
    super(fileManifest, symbolProvider, new CSharpWriter.CSharpWriterFactory());
  }
}

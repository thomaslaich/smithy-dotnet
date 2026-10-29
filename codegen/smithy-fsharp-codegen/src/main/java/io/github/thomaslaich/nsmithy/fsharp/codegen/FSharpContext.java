package io.github.thomaslaich.nsmithy.fsharp.codegen;

import java.util.List;
import software.amazon.smithy.build.FileManifest;
import software.amazon.smithy.codegen.core.CodegenContext;
import software.amazon.smithy.codegen.core.WriterDelegator;
import software.amazon.smithy.model.Model;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * What every F# generator works with. Generated code goes to {@link #files()}, which orders the
 * sections of each file; the writer delegator Smithy requires writes nothing.
 */
@SmithyUnstableApi
public record FSharpContext(
    Model model,
    FSharpSettings settings,
    FSharpSymbolProvider symbolProvider,
    FileManifest fileManifest,
    WriterDelegator<FSharpWriter> writerDelegator,
    List<FSharpIntegration> integrations,
    FSharpFiles files)
    implements CodegenContext<FSharpSettings, FSharpWriter, FSharpIntegration> {}

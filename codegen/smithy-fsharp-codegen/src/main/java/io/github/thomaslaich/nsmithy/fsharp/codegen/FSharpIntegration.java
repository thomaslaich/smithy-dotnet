package io.github.thomaslaich.nsmithy.fsharp.codegen;

import software.amazon.smithy.codegen.core.SmithyIntegration;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** Extends the F# generator. Implementations are found with {@link java.util.ServiceLoader}. */
@SmithyUnstableApi
public interface FSharpIntegration
    extends SmithyIntegration<FSharpSettings, FSharpWriter, FSharpContext> {}

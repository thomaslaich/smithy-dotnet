package io.github.thomaslaich.nsmithy.fsharp.codegen;

import io.github.thomaslaich.nsmithy.codegen.core.ProtocolInfo;
import java.util.List;
import software.amazon.smithy.codegen.core.SmithyIntegration;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** Extends the F# generator. Implementations are found with {@link java.util.ServiceLoader}. */
@SmithyUnstableApi
public interface FSharpIntegration
    extends SmithyIntegration<FSharpSettings, FSharpWriter, FSharpContext> {

  /** Protocols this integration adds to the built-in ones, which clients can then use. */
  default List<ProtocolInfo> protocols() {
    return List.of();
  }
}

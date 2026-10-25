package io.github.thomaslaich.nsmithy.codegen.core;

import java.util.Objects;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** A protocol whose runtime implementation every .NET language calls the same way. */
@SmithyUnstableApi
public record DotnetProtocol(
    ShapeId trait, String name, String runtimeTypeName, boolean eventStreams, boolean server)
    implements ProtocolInfo {

  public DotnetProtocol {
    Objects.requireNonNull(trait, "trait");
    Objects.requireNonNull(name, "name");
    Objects.requireNonNull(runtimeTypeName, "runtimeTypeName");
  }
}

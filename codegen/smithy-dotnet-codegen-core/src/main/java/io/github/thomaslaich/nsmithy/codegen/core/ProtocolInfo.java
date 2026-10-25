package io.github.thomaslaich.nsmithy.codegen.core;

import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/** A request/response protocol a generator can generate a client for. */
@SmithyUnstableApi
public interface ProtocolInfo {

  /** The protocol trait, such as {@code aws.protocols#restJson1}. */
  ShapeId trait();

  /** The name generated identifiers use, such as {@code RestJson1}. */
  String name();

  /** The full name of the .NET {@code IProtocol} implementation. */
  String runtimeTypeName();

  /** Whether the runtime protocol binds event-stream operations. */
  boolean eventStreams();

  /** Whether the runtime protocol also serves requests. */
  boolean server();
}

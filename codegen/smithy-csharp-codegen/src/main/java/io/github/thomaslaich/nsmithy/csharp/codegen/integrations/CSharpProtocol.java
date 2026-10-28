package io.github.thomaslaich.nsmithy.csharp.codegen.integrations;

import io.github.thomaslaich.nsmithy.codegen.core.ProtocolInfo;
import java.util.Objects;
import java.util.regex.Pattern;
import software.amazon.smithy.codegen.core.Symbol;
import software.amazon.smithy.model.shapes.ShapeId;
import software.amazon.smithy.utils.SmithyUnstableApi;

/**
 * What code generation needs from a request/response protocol. Generated clients and servers call
 * the runtime protocol, which serializes from schemas, so a protocol needs no generated code of its
 * own.
 *
 * @param trait the protocol trait, such as {@code aws.protocols#restJson1}
 * @param name used in generated identifiers, such as {@code RestJson1}
 * @param runtimeType the C# {@code IProtocol} implementation
 * @param eventStreams whether the runtime protocol binds event-stream operations
 * @param server whether the runtime protocol also serves requests
 */
@SmithyUnstableApi
public record CSharpProtocol(
    ShapeId trait, String name, Symbol runtimeType, boolean eventStreams, boolean server)
    implements ProtocolInfo {

  private static final Pattern IDENTIFIER = Pattern.compile("[A-Za-z_][A-Za-z0-9_]*");

  public CSharpProtocol {
    Objects.requireNonNull(trait, "trait");
    Objects.requireNonNull(name, "name");
    Objects.requireNonNull(runtimeType, "runtimeType");
    if (!IDENTIFIER.matcher(name).matches()) {
      throw new IllegalArgumentException(
          "Protocol name '" + name + "' for " + trait + " is not a C# identifier.");
    }
  }

  /** A protocol registered in the shared core, with its runtime type as a C# symbol. */
  public static CSharpProtocol of(ProtocolInfo protocol) {
    return new CSharpProtocol(
        protocol.trait(),
        protocol.name(),
        runtimeType(protocol.runtimeTypeName()),
        protocol.eventStreams(),
        protocol.server());
  }

  @Override
  public String runtimeTypeName() {
    return runtimeType.getFullName();
  }

  /** A runtime type from its full C# name, such as {@code "Acme.Protocols.MsgPackProtocol"}. */
  public static Symbol runtimeType(String fullName) {
    int separator = fullName.lastIndexOf('.');
    if (separator <= 0) {
      throw new IllegalArgumentException("'" + fullName + "' is not a namespace-qualified name.");
    }
    return Symbol.builder()
        .name(fullName.substring(separator + 1))
        .namespace(fullName.substring(0, separator), ".")
        .build();
  }
}

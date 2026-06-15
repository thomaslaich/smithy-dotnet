$version: "2"

namespace example.hello

use smithy.protocols#rpcv2Cbor

/// A simple greeting service built on the rpcv2Cbor protocol.
@rpcv2Cbor
service HelloService {
    version: "2026-01-01"
    operations: [SayHello]
}

/// Returns a greeting for the given name.
operation SayHello {
    input := {
        /// The name to greet.
        @required
        name: String
    }

    output := {
        /// Who is greeting.
        @required
        from: String

        /// The greeting message.
        @required
        message: String
    }

    errors: [InvalidName]
}

/// Returned when the provided name is not acceptable.
@error("client")
structure InvalidName {
    message: String
}

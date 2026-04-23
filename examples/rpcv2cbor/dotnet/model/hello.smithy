$version: "2"

namespace example.hello

use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service HelloService {
    version: "2026-04-23"
    operations: [SayHello]
}

operation SayHello {
    input := {
        @required
        name: String
    }

    output := {
        @required
        from: String

        @required
        message: String
    }

    errors: [InvalidName]
}

@error("client")
structure InvalidName {
    message: String
}

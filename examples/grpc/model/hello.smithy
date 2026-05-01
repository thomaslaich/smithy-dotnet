$version: "2"

namespace example.hello

use alloy#simpleRestJson
use alloy.proto#grpc
use alloy.proto#protoIndex

@simpleRestJson
@grpc
service HelloService {
    version: "2026-04-21"
    operations: [SayHello, Ping]
}

@http(method: "GET", uri: "/hello/{name}")
@readonly
operation SayHello {
    input := {
        @required
        @protoIndex(1)
        @httpLabel
        name: String
    }

    output := {
        @required
        @protoIndex(1)
        @httpHeader("x-smithy-service")
        from: String

        @required
        @protoIndex(2)
        message: String
    }
}

@http(method: "POST", uri: "/ping")
operation Ping {
    input := {
        @required
        @protoIndex(1)
        name: String
    }

    output := {
        @required
        @protoIndex(1)
        @httpHeader("x-smithy-service")
        from: String

        @required
        @protoIndex(2)
        message: String
    }
}

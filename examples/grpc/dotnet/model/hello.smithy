$version: "2"

namespace example.hello

use alloy#simpleRestJson
use alloy.proto#grpc

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
        @httpLabel
        name: String
    }

    output := {
        @required
        @httpHeader("x-smithy-service")
        from: String

        @required
        message: String
    }
}

@http(method: "POST", uri: "/ping")
operation Ping {
    input := {
        @required
        name: String
    }

    output := {
        @required
        @httpHeader("x-smithy-service")
        from: String

        @required
        message: String
    }
}

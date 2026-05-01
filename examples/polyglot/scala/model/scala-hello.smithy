$version: "2"

namespace example.scala.hello

use alloy#simpleRestJson
use alloy.proto#grpc
use alloy.proto#protoIndex

@simpleRestJson
@grpc
service HelloService {
    version: "2024-01-01"
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
        message: String

        /// The name of the service that handled this request.
        @required
        @protoIndex(2)
        from: String
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
        message: String

        @required
        @protoIndex(2)
        from: String
    }
}

$version: "2"

namespace example.scala.hello

use alloy#simpleRestJson

@simpleRestJson
service HelloService {
    version: "2024-01-01"
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
        message: String

        /// The name of the service that handled this request.
        @required
        from: String
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
        message: String

        @required
        from: String
    }
}

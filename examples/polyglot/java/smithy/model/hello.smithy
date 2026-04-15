$version: "2"

namespace example.hello

use aws.protocols#restJson1

@restJson1
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
        /// Base URL of the target HelloService to call (e.g. "http://scala-service:8080").
        @required
        targetUrl: String

        /// The name to pass to the target service's SayHello operation.
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

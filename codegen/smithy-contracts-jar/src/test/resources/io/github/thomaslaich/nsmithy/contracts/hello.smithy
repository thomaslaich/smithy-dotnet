$version: "2"

namespace example.contracts

use aws.protocols#restJson1

@restJson1
service HelloService {
    version: "2026-11-01"
    operations: [Greet]
}

@readonly
@http(method: "GET", uri: "/greet")
operation Greet {
    output := {
        message: String
    }
}

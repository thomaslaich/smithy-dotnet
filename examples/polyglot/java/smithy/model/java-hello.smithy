$version: "2"

namespace example.java.hello

use aws.protocols#restJson1

@restJson1
service HelloService {
    version: "2024-01-01"
    operations: [
        SayHello
        ShoutHello
    ]
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

@http(method: "POST", uri: "/shout")
operation ShoutHello {
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

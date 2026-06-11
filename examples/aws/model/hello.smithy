$version: "2"

namespace example.hello

use aws.protocols#restXml

/// A simple greeting service built on the restXml protocol.
@restXml
service HelloXmlService {
    version: "2026-01-01"
    operations: [SayHelloXml]
}

@http(method: "POST", uri: "/xml/hello")
operation SayHelloXml {
    input := {
        @required
        name: String
    }

    output := {
        @required
        @xmlName("Service")
        from: String

        @required
        @xmlName("Message")
        message: String
    }
}

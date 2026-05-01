$version: "2"

namespace example.hello

use aws.protocols#restXml
use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service HelloService {
    version: "2026-04-23"
    operations: [SayHello]
}

@restXml
service HelloXmlService {
    version: "2026-04-23"
    operations: [SayHelloXml]
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

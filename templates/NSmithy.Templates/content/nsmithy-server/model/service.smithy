$version: "2"

namespace example.hello

//#if (IsRestJson1)
use aws.protocols#restJson1
//#elseif (IsSimpleRestJson)
use alloy#simpleRestJson
//#elseif (IsGrpc)
use alloy.proto#grpc
use alloy.proto#protoIndex
//#endif

/// A hello world service. Replace this with your service definition.
//#if (IsRestJson1)
@restJson1
//#elseif (IsSimpleRestJson)
@simpleRestJson
//#elseif (IsGrpc)
@grpc
//#endif
service HelloService {
    version: "2006-03-01"
    operations: [SayHello]
}

/// Returns a greeting for the given name.
//#if (IsHttp)
@readonly
@http(method: "GET", uri: "/hello/{name}")
//#endif
operation SayHello {
    input := {
        @required
//#if (IsGrpc)
        @protoIndex(1)
//#endif
//#if (IsHttp)
        @httpLabel
//#endif
        name: String
    }
    output := {
        @required
//#if (IsGrpc)
        @protoIndex(1)
//#endif
        message: String
    }
}

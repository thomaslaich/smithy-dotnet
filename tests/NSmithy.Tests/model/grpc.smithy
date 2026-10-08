$version: "2"

namespace nsmithy.tests.grpc

use alloy.proto#protoIndex

/// Shapes the grpc tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [SayHello, WatchConstrained, Watch, Upload, Chat, UseShapes]
}

operation SayHello {
    input := {
        @required
        @protoIndex(1)
        message: String
    }
    output := {
        @protoIndex(1)
        message: String
    }
    errors: [ThrottlingError]
}

operation WatchConstrained {
    input := {
        @required
        @protoIndex(1)
        @length(min: 2, max: 10)
        message: String
    }
    output := {
        events: ChatEvent
    }
}

operation Watch {
    input := {
        @required
        @protoIndex(1)
        message: String
    }
    output := {
        events: ChatEvent
    }
}

operation Upload {
    input := {
        events: ChatEvent
    }
    output := {
        @required
        @protoIndex(1)
        message: String
    }
}

operation Chat {
    input := {
        events: ChatEvent
    }
    output := {
        events: ChatEvent
    }
}

operation UseShapes {
    input := {
        future: FutureChatEvent
    }
}

@error("client")
@httpError(429)
structure ThrottlingError {
    @required
    @protoIndex(1)
    message: String
}

@streaming
union ChatEvent {
    @protoIndex(1)
    message: Echo
}

structure Echo {
    @required
    @protoIndex(1)
    message: String
}

/// Carries ChatEvent's case at a field number ChatEvent does not know, standing in for a newer
/// peer's added oneof case.
union FutureChatEvent {
    @protoIndex(99)
    future: Echo
}

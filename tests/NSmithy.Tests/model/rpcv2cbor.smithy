$version: "2"

namespace nsmithy.tests.rpcv2cbor

/// Shapes the rpcv2cbor tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [Watch, WatchWithInitial, Upload, UploadWithInitial, Chat, Talk, Converse]
}

structure Echo {
    @required
    message: String
}

@streaming
union ChatEvent {
    message: Echo
}

operation Watch {
    input := {
        @required
        message: String
    }
    output := {
        @required
        events: ChatEvent
    }
}

operation WatchWithInitial {
    input := {
        @required
        message: String
    }
    output := {
        @required
        name: String

        @required
        events: ChatEvent
    }
}

operation Upload {
    input := {
        @required
        events: ChatEvent
    }
    output := {
        @required
        message: String
    }
}

operation UploadWithInitial {
    input := {
        @required
        name: String

        @required
        events: ChatEvent
    }
    output := {
        @required
        message: String
    }
}

operation Chat {
    input := {
        @required
        events: ChatEvent
    }
    output := {
        @required
        events: ChatEvent
    }
}

operation Talk {
    input := {
        @required
        @length(min: 2, max: 10)
        name: String

        @required
        events: ChatEvent
    }
    output := {
        @required
        message: String
    }
}

operation Converse {
    input := {
        @required
        @length(min: 2, max: 10)
        name: String

        @required
        events: ChatEvent
    }
    output := {
        @required
        events: ChatEvent
    }
}

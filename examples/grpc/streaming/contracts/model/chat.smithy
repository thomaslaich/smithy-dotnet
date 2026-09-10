$version: "2"

namespace example.chat

use alloy.proto#grpc
use alloy.proto#protoIndex
use alloy.proto#protoNumType
use smithy.api#streaming

@grpc
service ChatService {
    version: "2026-06-24"
    operations: [WatchRoom, UploadTranscript, Chat]
}

/// Server-streaming: one request, many events.
operation WatchRoom {
    input := {
        @required
        @protoIndex(1)
        room: String
    }
    output := {
        events: ChatEvent
    }
}

/// Client-streaming: many events, one summary.
operation UploadTranscript {
    input := {
        events: ChatEvent
    }
    output := {
        @required
        @protoIndex(1)
        @protoNumType("UNSIGNED")
        accepted: Integer
    }
}

/// Bidirectional streaming: many events in both directions.
operation Chat {
    input := {
        events: ChatEvent
    }
    output := {
        events: ChatEvent
    }
}

@streaming
union ChatEvent {
    @protoIndex(1)
    message: MessageEvent
}

structure MessageEvent {
    @required
    @protoIndex(1)
    user: String

    @required
    @protoIndex(2)
    text: String
}

$version: "2"

namespace example.chat

use smithy.api#streaming
use smithy.protocols#rpcv2Cbor

@rpcv2Cbor
service ChatService {
    version: "2026-07-28"
    operations: [WatchRoom, UploadTranscript, Chat]
}

/// Server-streaming: one request, many events.
operation WatchRoom {
    input := {
        @required
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
    message: MessageEvent
}

structure MessageEvent {
    @required
    user: String

    @required
    text: String
}

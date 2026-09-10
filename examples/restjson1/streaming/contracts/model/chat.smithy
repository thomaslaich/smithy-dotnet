$version: "2"

namespace example.chat

use aws.protocols#restJson1
use smithy.api#streaming

@restJson1
service ChatService {
    version: "2026-07-30"
    operations: [WatchRoom, UploadTranscript, Chat]
}

/// Server-streaming over restJson1: one request, many AWS event-stream frames.
@readonly
@http(method: "GET", uri: "/rooms/{room}/watch")
operation WatchRoom {
    input := {
        @required
        @httpLabel
        room: String
    }
    output := {
        @httpPayload
        events: ChatEvent
    }
}

/// Client-streaming over restJson1: many AWS event-stream frames, one JSON summary.
@http(method: "POST", uri: "/transcripts")
operation UploadTranscript {
    input := {
        @httpPayload
        events: ChatEvent
    }
    output := {
        @required
        accepted: Integer
    }
}

/// Bidirectional streaming over restJson1: AWS event-stream frames in both directions.
@http(method: "POST", uri: "/rooms/{room}/chat")
operation Chat {
    input := {
        @required
        @httpLabel
        room: String

        @httpPayload
        events: ChatEvent
    }
    output := {
        @required
        @httpHeader("X-Room")
        room: String

        @httpPayload
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

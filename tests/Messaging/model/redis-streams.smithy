$version: "2"

namespace tests.redisstreams

use bote#command
use bote#event
use bote#redisStreamAdd
use bote#redisStreamRead
use bote#redisStreamsJson

/// A durable chat contract: commands and events use separate Redis streams.
@title("Redis Chat API")
@redisStreamsJson
service ChatRoom {
    version: "1.0.0"
    operations: [PostMessage, ReadMessages]
}

/// Ask the chat owner to post a message.
@redisStreamAdd(stream: "tests.streams:commands", maxLen: 10000)
operation PostMessage {
    input: PostMessageCommand
}

/// Follow messages emitted by the chat owner.
@redisStreamRead(stream: "tests.streams:events", maxLen: 10000)
operation ReadMessages {
    output := {
        messages: ChatEvents
    }
}

@command
structure PostMessageCommand {
    roomId: String
    userId: String

    @length(min: 1, max: 4000)
    body: String
}

@event
structure MessagePosted {
    roomId: String
    userId: String

    @length(min: 1, max: 4000)
    body: String

    sentAt: Timestamp
}

@streaming
union ChatEvents {
    messagePosted: MessagePosted
}

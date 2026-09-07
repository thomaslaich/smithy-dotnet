$version: "2"

namespace examples.redis.chat

use bote#event
use bote#redisStreamRead
use bote#redisStreamsJson

/// Chat participants publish and independently read a shared Redis event stream.
@title("Redis Chat API")
@redisStreamsJson
service ChatRoom {
    version: "1.0.0"
    operations: [ReadMessages]
}

/// Follow messages published directly by chat participants.
@redisStreamRead(stream: "chat:events", maxLen: 10000)
operation ReadMessages {
    output := {
        messages: ChatEvents
    }
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

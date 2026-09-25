$version: "2"

namespace tests.redispubsub

use bote#command
use bote#event
use bote#redisPublish
use bote#redisSubscribe
use bote#redisPubSubJson

/// A durable chat contract: commands and events use separate Redis streams.
@title("Redis Chat API")
@redisPubSubJson
service ChatRoom {
    version: "1.0.0"
    operations: [PostMessage, ReadMessages]
}

/// Ask the chat owner to post a message.
@redisPublish(channel: "tests.pubsub:commands")
operation PostMessage {
    input: PostMessageCommand
}

/// Follow messages emitted by the chat owner.
@redisSubscribe(channel: "tests.pubsub:events")
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

$version: "2"

namespace examples.redis.inventory

use bote#command
use bote#redisStreamAdd
use bote#redisStreamsJson
use bote#reply

/// A unary inventory query over a durable Redis request stream.
@title("Redis Inventory API")
@redisStreamsJson
service Inventory {
    version: "1.0.0"
    operations: [GetStock]
}

@redisStreamAdd(stream: "inventory:queries", maxLen: 10000)
operation GetStock {
    input: GetStockRequest
    output: GetStockReply
}

@command
structure GetStockRequest {
    productId: String
}

@reply
structure GetStockReply {
    productId: String
    available: Integer
}

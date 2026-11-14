$version: "2"

namespace nsmithy.bench.grpc

use alloy.proto#grpc
use alloy.proto#protoIndex
use smithy.framework#ValidationException

@grpc
service GrpcBenchmarkService {
    version: "1.0"
    operations: [GetItem, ListItems]
    errors: [ValidationException]
}

operation GetItem {
    input := {
        @required
        @protoIndex(1)
        id: String
    }
    output := {
        @required
        @protoIndex(1)
        item: Item
    }
}

operation ListItems {
    input := {
        @required
        @protoIndex(1)
        count: Integer
    }
    output := {
        @required
        @protoIndex(1)
        items: ItemList
    }
}

structure Item {
    @required
    @protoIndex(1)
    id: String

    @required
    @protoIndex(2)
    name: String

    @required
    @protoIndex(3)
    priceCents: Integer

    @required
    @protoIndex(4)
    inStock: Boolean

    @required
    @protoIndex(5)
    tags: TagList
}

list ItemList {
    member: Item
}

list TagList {
    member: String
}

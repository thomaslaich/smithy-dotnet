$version: "2"

namespace nsmithy.tests.proto

use alloy.proto#protoIndex
use alloy.proto#protoInlinedOneOf
use alloy.proto#protoNumType

/// Shapes the proto tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [UseShapes]
}

operation UseShapes {
    input := {
        simple: Simple
        signed: Signed
        fixed: Fixed
        repeated: Repeated
        repeatedSigned: RepeatedSigned
        intMapHolder: IntMapHolder
        emptyCollections: EmptyCollections
        book: Book
        painting: Painting
        attributed: Attributed
        query: Query
        envelope: Envelope
        unnumbered: Unnumbered
    }
}

structure Simple {
    @required
    @protoIndex(1)
    name: String

    @required
    @protoIndex(2)
    value: Integer
}

structure Signed {
    @required
    @protoIndex(1)
    @protoNumType("SIGNED")
    a: Integer
}

structure Fixed {
    @required
    @protoIndex(1)
    @protoNumType("FIXED")
    a: Integer
}

list IntList {
    member: Integer
}

structure Repeated {
    @required
    @protoIndex(1)
    nums: IntList
}

list SignedIntList {
    @protoNumType("SIGNED")
    member: Integer
}

structure RepeatedSigned {
    @required
    @protoIndex(1)
    nums: SignedIntList
}

map SignedIntMap {
    key: String

    @protoNumType("SIGNED")
    value: Integer
}

structure IntMapHolder {
    @required
    @protoIndex(1)
    values: SignedIntMap
}

map StringMap {
    key: String
    value: String
}

structure EmptyCollections {
    @required
    @protoIndex(1)
    nums: IntList

    @required
    @protoIndex(2)
    metadata: StringMap
}

intEnum Category {
    UNSPECIFIED = 0
    FICTION = 1
    SCIENCE = 3
}

structure Nested {
    @required
    @protoIndex(1)
    value: Integer
}

list Tags {
    member: String
}

structure Book {
    @required
    @protoIndex(1)
    id: String

    @protoIndex(4)
    @protoNumType("UNSIGNED")
    pageCount: Integer

    @protoIndex(5)
    @protoNumType("FIXED")
    checksum: Long

    @required
    @protoIndex(8)
    tags: Tags

    @required
    @protoIndex(9)
    metadata: StringMap

    @protoIndex(10)
    detail: Nested

    @required
    @protoIndex(7)
    cat: Category

    @protoIndex(11)
    publishedAt: Timestamp
}

enum Color {
    RED
    GREEN
    BLUE
}

structure Painting {
    @required
    @protoIndex(1)
    shade: Color
}

@sparse
map Attrs {
    key: String
    value: String
}

structure Attributed {
    @required
    @protoIndex(1)
    attrs: Attrs
}

@protoInlinedOneOf
union Filter {
    @protoIndex(3)
    byId: String

    @protoIndex(4)
    byNum: Integer
}

structure Query {
    @protoIndex(1)
    page: Integer

    @protoIndex(2)
    filter: Filter
}

structure Envelope {
    @required
    @protoIndex(1)
    payload: Document
}

/// A modeled shape with no @protoIndex. Alloy rejects a shape that numbers only some members.
structure Unnumbered {
    @required
    name: String

    @required
    value: Integer
}

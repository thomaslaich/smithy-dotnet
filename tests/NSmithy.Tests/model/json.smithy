$version: "2"

namespace nsmithy.tests.json

use smithy.framework#ValidationException

/// Shapes the json tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        UseShapes
    ]
    errors: [ValidationException]
}

operation UseShapes {
    input := {
        scalars: Scalars
        profile: Profile
        jsonNamedProfile: JsonNamedProfile
        bag: Bag
        timeline: Timeline
        timestampRecord: TimestampRecord
        person: Person
        deployment: Deployment
        workItem: WorkItem
        defaulted: Defaulted
        treeNode: TreeNode
        choice: Choice
        order: Order
        requiredPerson: RequiredPerson
        arbitraryPrecision: ArbitraryPrecision
        figure: Figure
        corrected: Corrected
        correctedChoice: CorrectedChoice
        wide: Wide
    }
}

structure Scalars {
    @required
    text: String

    @required
    count: Integer

    @required
    big: Long

    @required
    ratio: Double

    @required
    flag: Boolean

    @required
    data: Blob
}

structure Profile {
    @required
    name: String

    nickname: String
}

structure JsonNamedProfile {
    @required
    @jsonName("displayName")
    name: String

    nickname: String
}

structure Bag {
    @required
    tags: Tags

    @required
    counts: Counts
}

list Tags {
    member: String
}

map Counts {
    key: String
    value: Integer
}

structure Timeline {
    @required
    events: Events
}

list Events {
    @timestampFormat("date-time")
    member: Timestamp
}

structure TimestampRecord {
    @required
    @timestampFormat("date-time")
    created: Timestamp
}

structure ArbitraryPrecision {
    @required
    integer: BigInteger

    @required
    decimal: BigDecimal
}

structure Address {
    @required
    city: String
}

structure Person {
    @required
    name: String

    @required
    address: Address
}

enum Status {
    ACTIVE
}

structure Deployment {
    @required
    name: String

    @required
    status: Status
}

intEnum Priority {
    LOW = 1
    HIGH = 2
}

structure WorkItem {
    @required
    title: String

    @required
    priority: Priority
}

structure Defaulted {
    count: Integer = 7
    labels: DefaultedLabels = []
}

list DefaultedLabels {
    member: String
}

structure TreeNode {
    @required
    value: String

    children: TreeNodeList
}

list TreeNodeList {
    member: TreeNode
}

union Choice {
    stringValue: String
    integerValue: Integer
}

structure Order {
    @required
    buyer: Customer
}

structure Customer {
    @required
    name: String
}

structure RequiredPerson {
    @required
    name: String
}

@alloy#discriminated("kind")
union Figure {
    circle: Circle
    square: Square
}

structure Circle {
    radius: Integer
}

structure Square {
    side: Integer
}

/// One required member of each kind, for a client filling the ones a response leaves out.
structure Corrected {
    @required
    text: String

    @required
    count: Integer

    @required
    flag: Boolean

    @required
    at: Timestamp

    @required
    data: Blob

    @required
    tags: Tags

    @required
    counts: Counts

    @required
    status: Status

    @required
    priority: Priority

    @required
    buyer: Customer

    @required
    kept: String = "kept"

    note: String
}

structure CorrectedChoice {
    @required
    choice: Choice
}

/// More required members than one presence word holds.
structure Wide {
    @required
    m0: Integer

    @required
    m1: Integer

    @required
    m2: Integer

    @required
    m3: Integer

    @required
    m4: Integer

    @required
    m5: Integer

    @required
    m6: Integer

    @required
    m7: Integer

    @required
    m8: Integer

    @required
    m9: Integer

    @required
    m10: Integer

    @required
    m11: Integer

    @required
    m12: Integer

    @required
    m13: Integer

    @required
    m14: Integer

    @required
    m15: Integer

    @required
    m16: Integer

    @required
    m17: Integer

    @required
    m18: Integer

    @required
    m19: Integer

    @required
    m20: Integer

    @required
    m21: Integer

    @required
    m22: Integer

    @required
    m23: Integer

    @required
    m24: Integer

    @required
    m25: Integer

    @required
    m26: Integer

    @required
    m27: Integer

    @required
    m28: Integer

    @required
    m29: Integer

    @required
    m30: Integer

    @required
    m31: Integer

    @required
    m32: Integer

    @required
    m33: Integer

    @required
    m34: Integer

    @required
    m35: Integer

    @required
    m36: Integer

    @required
    m37: Integer

    @required
    m38: Integer

    @required
    m39: Integer

    @required
    m40: Integer

    @required
    m41: Integer

    @required
    m42: Integer

    @required
    m43: Integer

    @required
    m44: Integer

    @required
    m45: Integer

    @required
    m46: Integer

    @required
    m47: Integer

    @required
    m48: Integer

    @required
    m49: Integer

    @required
    m50: Integer

    @required
    m51: Integer

    @required
    m52: Integer

    @required
    m53: Integer

    @required
    m54: Integer

    @required
    m55: Integer

    @required
    m56: Integer

    @required
    m57: Integer

    @required
    m58: Integer

    @required
    m59: Integer

    @required
    m60: Integer

    @required
    m61: Integer

    @required
    m62: Integer

    @required
    m63: Integer

    @required
    m64: Integer

    @required
    m65: Integer

    @required
    m66: Integer

    @required
    m67: Integer

    @required
    m68: Integer

    @required
    m69: Integer
}

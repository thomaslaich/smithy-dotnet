$version: "2"

namespace nsmithy.tests.json

/// Shapes the json tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        UseShapes
    ]
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

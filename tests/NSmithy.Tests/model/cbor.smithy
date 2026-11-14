$version: "2"

namespace nsmithy.tests.cbor

use smithy.framework#ValidationException

/// Shapes the cbor tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [UseShapes]
    errors: [ValidationException]
}

operation UseShapes {
    input := {
        person: Person
    }
}

structure Address {
    @required
    city: String
}

structure Person {
    @required
    name: String

    @required
    age: Integer

    @required
    address: Address
}

$version: "2"

namespace nsmithy.tests.awsquery

/// Shapes the awsquery tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "2020-01-08"
    operations: [SendGreeting]
}

operation SendGreeting {
    input: QueryInput
    output: GreetingOutput
    errors: [GreetingError]
}

structure QueryInput {
    Text: String
    When: Timestamp
    Items: StringList

    @xmlFlattened
    FlatItems: StringList

    Tags: StringMap
    Nested: Nested
}

structure Nested {
    Value: String
}

structure GreetingOutput {
    Greeting: String
}

@error("client")
@httpError(400)
structure GreetingError {
    Message: String
    Detail: String
}

list StringList {
    member: String
}

map StringMap {
    key: String
    value: String
}

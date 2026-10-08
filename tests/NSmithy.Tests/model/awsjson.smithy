$version: "2"

namespace nsmithy.tests.awsjson

/// Shapes the awsjson tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [Ping]
}

operation Ping {
    input := {}
    output := {}
    errors: [ThrottledError]
}

@error("client")
@httpError(429)
structure ThrottledError {
    reason: String
    retryAfter: Integer
}

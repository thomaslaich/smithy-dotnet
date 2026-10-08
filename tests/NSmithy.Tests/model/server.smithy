$version: "2"

namespace nsmithy.tests.server

/// Shapes the server tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        CreateUser
    ]
}

@http(method: "POST", uri: "/users")
operation CreateUser {
    input := {
        @required
        @length(min: 3)
        name: String
    }

    output := {
        @required
        name: String
    }
}

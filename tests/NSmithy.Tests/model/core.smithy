$version: "2"

namespace nsmithy.tests.core

/// Shapes the core tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        UpdateUser
        GetUser
        UseShapes
    ]
}

@http(method: "PUT", uri: "/users/{userId}")
@idempotent
operation UpdateUser {
    input := {
        @required
        @httpLabel
        userId: String

        @httpHeader("X-Request-Token")
        requestToken: String

        @required
        displayName: String
    }
    output := {}
}

@readonly
operation GetUser {
    errors: [
        BadRequest
    ]
}

@error("client")
@httpError(400)
structure BadRequest {
    message: String
}

operation UseShapes {
    input := {
        visitorInput: VisitorInput
        dateInput: DateInput
        timestampPayload: TimestampPayload
        status: Status
    }
}

structure VisitorInput {
    @required
    name: String

    @required
    age: Integer
}

@timestampFormat("date-time")
timestamp DateTime

structure DateInput {
    @required
    @timestampFormat("epoch-seconds")
    created: DateTime
}

structure TimestampPayload {
    @required
    @timestampFormat("date-time")
    @xmlName("CreatedAt")
    value: Timestamp
}

enum Status {
    ACTIVE
    INACTIVE
}

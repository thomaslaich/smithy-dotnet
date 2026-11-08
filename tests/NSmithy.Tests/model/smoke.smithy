$version: "2"

namespace nsmithy.tests.smoke

use aws.protocols#restJson1
use smithy.test#smokeTests

@restJson1
service Jobs {
    version: "2024-01-01"
    operations: [GetJob]
    errors: [Throttled]
}

@readonly
@http(method: "GET", uri: "/jobs/{id}")
@smokeTests([
    {
        id: "GetJobSucceeds"
        params: { id: "job-1" }
        tags: ["read"]
        expect: { success: {} }
    }
    {
        id: "GetJobIsMissing"
        params: { id: "missing" }
        expect: { failure: { errorId: JobNotFound } }
    }
    {
        id: "GetJobIsExpired"
        params: { id: "expired" }
        expect: { failure: { errorId: JobExpired } }
    }
    {
        id: "GetJobFails"
        params: { id: "invalid" }
        expect: { failure: {} }
    }
])
operation GetJob {
    input := {
        @required
        @httpLabel
        id: String
    }
    output := {
        status: String
    }
    errors: [JobNotFound]
}

@error("client")
@httpError(404)
structure JobNotFound {}

/// Not bound to the service, so its smoke test matches it by error code.
@error("client")
structure JobExpired {}

@error("client")
@httpError(429)
structure Throttled {}

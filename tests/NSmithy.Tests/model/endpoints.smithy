$version: "2"

metadata suppressions = [
    {
        id: "UnstableTrait"
        namespace: "nsmithy.tests.endpoints"
    }
]

namespace nsmithy.tests.endpoints

use aws.protocols#restJson1
use smithy.rules#clientContextParams
use smithy.rules#contextParam
use smithy.rules#endpointRuleSet

/// A service whose endpoints come from its rules: regional and FIPS hosts, a custom endpoint, and
/// a per-bucket host bound from operation input.
@restJson1
@clientContextParams(
    Region: { type: "string", documentation: "The region to send requests to." }
    UseFips: { type: "boolean", documentation: "Use the FIPS endpoint." }
)
@endpointRuleSet({
    version: "1.0"
    parameters: {
        Region: { type: "String", required: false, documentation: "The region to send requests to." }
        UseFips: { type: "Boolean", required: true, default: false, documentation: "Use the FIPS endpoint." }
        Endpoint: { type: "String", required: false, builtIn: "SDK::Endpoint", documentation: "Custom endpoint." }
        Bucket: { type: "String", required: false, documentation: "The bucket a request targets." }
    }
    rules: [
        {
            conditions: [{ fn: "isSet", argv: [{ ref: "Endpoint" }] }]
            type: "tree"
            rules: [
                {
                    conditions: [{ fn: "booleanEquals", argv: [{ ref: "UseFips" }, true] }]
                    type: "error"
                    error: "FIPS and custom endpoint are not supported"
                }
                {
                    conditions: []
                    type: "endpoint"
                    endpoint: { url: { ref: "Endpoint" } }
                }
            ]
        }
        {
            conditions: [{ fn: "isSet", argv: [{ ref: "Region" }] }]
            type: "tree"
            rules: [
                {
                    conditions: [
                        { fn: "isSet", argv: [{ ref: "Bucket" }] }
                        { fn: "isValidHostLabel", argv: [{ ref: "Bucket" }, false] }
                    ]
                    type: "endpoint"
                    endpoint: {
                        url: "https://{Bucket}.storage.{Region}.example.com"
                        headers: { "x-bucket": ["{Bucket}"] }
                        properties: {
                            authSchemes: [{ name: "sigv4", signingName: "storage", signingRegion: "{Region}" }]
                        }
                    }
                }
                {
                    conditions: [{ fn: "booleanEquals", argv: [{ ref: "UseFips" }, true] }]
                    type: "endpoint"
                    endpoint: { url: "https://storage-fips.{Region}.example.com" }
                }
                {
                    conditions: []
                    type: "endpoint"
                    endpoint: { url: "https://storage.{Region}.example.com" }
                }
            ]
        }
        {
            conditions: []
            type: "error"
            error: "Region must be set"
        }
    ]
})
service Storage {
    version: "2024-01-01"
    operations: [
        ListBuckets
        GetObject
    ]
}

@readonly
@http(method: "GET", uri: "/")
operation ListBuckets {
    input := {}
    output := {}
}

@readonly
@http(method: "GET", uri: "/objects/{key}")
operation GetObject {
    input := {
        @required
        @httpLabel
        key: String

        @contextParam(name: "Bucket")
        @httpQuery("bucket")
        bucket: String
    }
    output := {}
}

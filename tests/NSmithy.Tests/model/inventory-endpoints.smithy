$version: "2"
metadata suppressions = [{ id: "UnstableTrait", namespace: "nsmithy.tests.inventory" }]
namespace nsmithy.tests.inventory

use alloy#simpleRestJson
use smithy.rules#clientContextParams
use smithy.rules#endpointRuleSet

@simpleRestJson
@clientContextParams(Environment: { type: "string" })
@endpointRuleSet({
    version: "1.0"
    parameters: {
        Environment: { type: "String", required: true, default: "test", documentation: "Deployment environment." }
        Endpoint: { type: "String", required: false, builtIn: "SDK::Endpoint", documentation: "Explicit service URL." }
    }
    rules: [
        {
            conditions: [{ fn: "isSet", argv: [{ ref: "Endpoint" }] }]
            type: "endpoint"
            endpoint: { url: { ref: "Endpoint" } }
        }
        {
            conditions: [{ fn: "stringEquals", argv: [{ ref: "Environment" }, "prod"] }]
            type: "endpoint"
            endpoint: { url: "https://inventory.prod.example.test" }
        }
        {
            conditions: [{ fn: "stringEquals", argv: [{ ref: "Environment" }, "test"] }]
            type: "endpoint"
            endpoint: { url: "https://inventory.test.example.test" }
        }
        { conditions: [], type: "error", error: "Environment must be prod or test" }
    ]
})
service Inventory {
    version: "1"
    operations: [GetStock]
}

@readonly
@http(method: "GET", uri: "/stock/{sku}")
operation GetStock {
    input := { @required @httpLabel sku: String }
    output := { @required quantity: Integer }
}

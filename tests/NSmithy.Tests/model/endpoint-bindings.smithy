$version: "2"

metadata suppressions = [{ id: "UnstableTrait", namespace: "nsmithy.tests.endpointbindings" }]

namespace nsmithy.tests.endpointbindings

use aws.protocols#restJson1
use smithy.rules#clientContextParams
use smithy.rules#contextParam
use smithy.rules#endpointRuleSet
use smithy.rules#operationContextParams
use smithy.rules#staticContextParams

@restJson1
@clientContextParams(
    Route: { type: "string" }
    Region: { type: "string" }
    Flag: { type: "boolean" }
)
@endpointRuleSet({
    version: "1.0"
    parameters: {
        Region: { type: "String", required: true }
        Flag: { type: "Boolean", required: true }
        Names: { type: "stringArray", required: true }
        Route: { type: "String", required: true, default: "default" }
    }
    rules: [{
        conditions: []
        type: "endpoint"
        endpoint: { url: "https://example.com/{Region}/{Route}" }
    }]
})
service Routing {
    version: "1"
    operations: [SelectRoute, StaticRoute]
}

structure RouteDetails { route: String }

@readonly
@http(method: "POST", uri: "/select")
@staticContextParams(Names: { value: [] })
@operationContextParams(Route: { path: "nested.route" })
operation SelectRoute {
    input := {
        @contextParam(name: "Route")
        @httpQuery("member")
        member: String
        nested: RouteDetails
    }
    output := {}
}

@readonly
@http(method: "POST", uri: "/static")
@staticContextParams(Route: { value: "static" }, Names: { value: [] })
@operationContextParams(Route: { path: "nested.route" })
operation StaticRoute {
    input := {
        @contextParam(name: "Route")
        @httpQuery("member")
        member: String
        nested: RouteDetails
    }
    output := {}
}

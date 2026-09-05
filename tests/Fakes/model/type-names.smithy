$version: "2"

namespace example.names

use aws.protocols#restJson1

// Compile regression coverage for references hidden by generated nested types,
// union variants, generic parameters, and a type named after a namespace root.
@restJson1
service Names {
    version: "1"
    operations: [RoundTrip]
    rename: { "example.other#Widget": "ForeignWidget" }
}

@http(method: "POST", uri: "/names")
operation RoundTrip {
    input: Payload
    output: Payload
}

structure Payload {
    helper: Builder
    serializer: ValueSerializer
    choice: Choice
    widgets: Widgets
    widgetMap: WidgetMap
    localWidget: Widget
    foreignWidget: example.other#Widget
    namespaceRoot: Example
    genericWriter: TWriter
    serverHelper: RoundTripJsonSchemas
}

structure Builder {}
structure ValueSerializer {}
structure Example {}
structure TWriter {}
structure RoundTripJsonSchemas {}
structure Widget {
    label: String
}

union Choice {
    widget: Widget
    generic: T
    unknownValue: Unknown
}

structure T {}
structure Unknown {}

list Widgets {
    member: Widget
}

map WidgetMap {
    key: String
    value: Widget
}

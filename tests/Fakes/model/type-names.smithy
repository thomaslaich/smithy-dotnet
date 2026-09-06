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

@paginated(inputToken: "nextToken", outputToken: "nextToken", items: "widgets")
@http(method: "POST", uri: "/names")
operation RoundTrip {
    input: Payload
    output: Payload
}

structure Payload {
    nextToken: String
    frameworkNames: FrameworkNames
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

// These model types must not capture imported framework references, including
// static calls, paginator parameters, and the EnumeratorCancellation attribute.
structure FrameworkNames {
    task: Task
    cancellation: CancellationToken
    enumerable: IAsyncEnumerable
    attributeName: EnumeratorCancellation
    client: HttpClient
    array: Array
    dictionary: Dictionary
    list: List
    exception: Exception
    uri: Uri
}

structure Task {}
structure CancellationToken {}
structure IAsyncEnumerable {}
structure EnumeratorCancellation {}
structure HttpClient {}
structure Array {}
structure Dictionary {}
structure List {}
structure Exception {}
structure Uri {}

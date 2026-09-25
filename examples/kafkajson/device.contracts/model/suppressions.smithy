$version: "2"

// bote @command structures are shared contract types, deliberately not
// @input-scoped DTOs (see bote's messaging-traits). smithy-docgen treats the
// resulting DANGER as fatal, so suppress it for the contract namespace.
metadata suppressions = [
    {
        id: "InputOutputStructureReuse"
        namespace: "examples.kafka.streetlights"
        reason: "bote @command payloads are first-class contract types shared across operations."
    }
]

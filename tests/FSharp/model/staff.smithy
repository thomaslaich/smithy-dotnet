$version: "2"

// A namespace only the service's errors refer to, and one that sorts after the service's, so the
// generated F# must order namespaces by service errors too.
namespace example.staff

/// The kitchen does not take orders right now.
@error("server")
@httpError(503)
structure KitchenClosed {
    message: String
}

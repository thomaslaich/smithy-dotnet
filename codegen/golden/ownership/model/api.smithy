$version: "2"

// The service, in a project that references the module and uses the shapes it generated.
namespace acme.shop

use aws.protocols#restJson1

@restJson1
service Shop {
    version: "2026-01-01"
    operations: [PlaceOrder]
}

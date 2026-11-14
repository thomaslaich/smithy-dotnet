$version: "2"

namespace example.shop

use aws.protocols#restJson1
use smithy.framework#ValidationException

/// The shop's HTTP API. Its operations are defined and implemented in the modules.
@restJson1
service Shop {
    version: "2026-11-01"
    operations: [PlaceOrder, CapturePayment]
    errors: [ValidationException]
}

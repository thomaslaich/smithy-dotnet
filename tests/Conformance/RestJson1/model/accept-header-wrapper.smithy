$version: "1.0"

namespace restjsonone.local

use aws.protocoltests.misc#AcceptHeaderStarService
use aws.protocols#restJson1
@restJson1
service AcceptHeaderStarHarness {
    version: "1"
    operations: [AcceptHeaderStarService]
}

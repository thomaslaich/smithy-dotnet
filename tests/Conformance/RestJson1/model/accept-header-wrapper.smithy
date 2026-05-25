$version: "1.0"

namespace restjsonone.local

use aws.protocoltests.misc#AcceptHeaderStarService
use aws.protocols#restJson1
use aws.protocoltests.restjson.validation#RecursiveStructures

@restJson1
service AcceptHeaderStarHarness {
    version: "1"
    operations: [AcceptHeaderStarService]
}

@restJson1
service RecursiveStructuresHarness {
    version: "1"
    operations: [RecursiveStructures]
}

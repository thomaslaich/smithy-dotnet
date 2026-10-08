$version: "2"

namespace nsmithy.tests.xml

/// Shapes the xml tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [UseShapes]
}

operation UseShapes {
    input := {
        catalog: Catalog
        namespacedCatalog: NamespacedCatalog
        buckets: ListAllMyBucketsResult
        person: Person
    }
}

structure Catalog {
    items: ItemList
}

@xmlName("Catalog")
@xmlNamespace(uri: "urn:example")
structure NamespacedCatalog {
    items: ItemList
}

list ItemList {
    member: String
}

structure S3Bucket {
    @xmlName("Name")
    name: String
}

list S3BucketList {
    @xmlName("Bucket")
    member: S3Bucket
}

structure ListAllMyBucketsResult {
    @xmlName("Buckets")
    buckets: S3BucketList
}

structure Address {
    @required
    city: String
}

structure Person {
    @required
    name: String

    @required
    age: Integer

    @required
    address: Address
}

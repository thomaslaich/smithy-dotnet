$version: "2"

namespace nsmithy.tests.naming

use aws.protocols#restJson1

/// Shapes whose names collide with generated C# names. Each one must still compile, and the
/// tests use the names the generator falls back to.
@restJson1
service Fixtures {
    version: "1"
    operations: [
        UseNames
    ]
}

@http(method: "POST", uri: "/names")
operation UseNames {
    input := {
        domainName: DomainName
        platformOutput: PlatformOutput
        selector: Selector
        operator: Operator
        level: Level
        equals: Equals
        pathElement: PathElement
        filter: Filter
        dataset: Dataset
        datasetSchema: DatasetSchema
    }
    output := {
        pathElement: PathElement
        filter: Filter
    }
}

/// A member named like its structure.
structure DomainName {
    domainName: String
    certificateArn: String
}

/// A member named like the schema's staged builder.
structure PlatformOutput {
    builder: String
}

/// Members named like inherited members.
structure Selector {
    equals: String
    toString: String
}

/// Enum values named like an inherited member or the members every string enum declares.
enum Operator {
    Equals = "EQ"
    Contains = "CO"
    Value = "VALUE"
    FromValue = "FROM_VALUE"
}

/// An int enum value named like an inherited member.
intEnum Level {
    Equals = 1
    ToString = 2
}

/// A shape named like an inherited member.
structure Equals {
    field: String
}

/// Union variants named like the members every union declares, or like the union itself.
union PathElement {
    index: Integer
    value: String
    unknown: String
    pathElement: String
}

/// Union variants named like the Unknown case's members and an inherited member.
union Filter {
    tag: String
    equals: String
    prefix: String
}

structure Dataset {
    name: String
}

/// A shape named like the schema class of another shape.
structure DatasetSchema {
    fields: String
}

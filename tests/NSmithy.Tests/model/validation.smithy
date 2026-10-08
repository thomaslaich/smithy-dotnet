$version: "2"

namespace nsmithy.tests.validation

/// Shapes the validation tests exercise, generated so the tests use the same schemas a consumer would.
service Fixtures {
    version: "1"
    operations: [
        Validate
        Upload
        LimitedUpload
    ]
}

operation Validate {
    input := {
        profile: Profile
        account: Account
        note: Note
        tree: TreeNode
        codes: Codes
        scores: Scores
        rangedScores: RangedScores
        measurement: Measurement
        blobs: Blobs
        colour: Colour
        rank: Rank
        palette: Palette
        baskets: Baskets
    }
}

operation Upload {
    input := {
        @required
        body: StreamingBlob

        @required
        @length(min: 2, max: 10)
        name: String
    }
}

operation LimitedUpload {
    input := {
        @required
        @length(min: 1, max: 4)
        body: StreamingBlob

        @required
        name: String
    }
}

@streaming
blob StreamingBlob

structure Profile {
    @required
    @length(min: 2, max: 10)
    @pattern("^[A-Za-z]+$")
    Name: String

    @required
    @range(min: 0, max: 150)
    Age: Integer

    @required
    Tags: Tags
}

@uniqueItems
list Tags {
    member: String
}

structure Account {
    @required
    profile: Profile
}

structure Note {
    Text: String
}

structure TreeNode {
    @required
    @length(min: 1, max: 5)
    Label: String

    Child: TreeNode
}

list Codes {
    @length(min: 2, max: 4)
    member: String
}

map Scores {
    @length(min: 2, max: 4)
    key: String

    @range(min: 0, max: 10)
    value: Integer
}

map RangedScores {
    key: String

    @range(min: 0, max: 10)
    value: Integer
}

structure Measurement {
    @range(min: 0, max: 10)
    rating: Integer

    @required
    @range(min: 0, max: 10)
    score: Double
}

@uniqueItems
list Blobs {
    member: Blob
}

enum Colour {
    RED
    GREEN
}

intEnum Rank {
    First = 1
    Second = 2
}

map Palette {
    key: PaletteColour
    value: String
}

enum PaletteColour {
    RED
    GREEN

    @internal
    PUCE
}

structure Basket {
    @required
    items: Items
}

list Items {
    member: String
}

@uniqueItems
list Baskets {
    member: Basket
}

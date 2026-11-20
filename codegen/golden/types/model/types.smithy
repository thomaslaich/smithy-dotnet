$version: "2"

// Every kind of shape, generated without a service, as a contracts project does.
namespace example.types

/// A dish on the menu.
structure Dish {
    @required
    name: String

    /// Shown under the name.
    description: String

    @default(1)
    portions: Integer

    @required
    price: Price

    tags: Tags

    allergens: Allergens

    course: Course

    spice: SpiceLevel

    @timestampFormat("date-time")
    addedAt: Timestamp

    photo: Blob
}

structure Price {
    @required
    amount: BigDecimal

    @required
    currency: String
}

/// How a dish is served.
union Course {
    starter: Unit
    main: MainCourse
    dessert: String
}

structure MainCourse {
    side: String
}

enum Allergen {
    GLUTEN = "gluten"
    NUTS = "nuts"
}

list Allergens {
    member: Allergen
}

@sparse
map Tags {
    key: String
    value: String
}

intEnum SpiceLevel {
    MILD = 1
    HOT = 2
}

structure Empty {}

/// The dish is sold out.
@error("client")
structure SoldOut {
    message: String

    @required
    dish: String
}

@error("server")
@retryable(throttling: true)
structure Busy {
    @required
    message: String
}

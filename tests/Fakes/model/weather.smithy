$version: "2"

namespace example.weather

use aws.protocols#restJson1

/// A trimmed copy of the examples' Weather service, used to exercise the
/// generated fakes (fake client and fake handler) end to end.
@restJson1
service Weather {
    version: "2006-03-01"
    operations: [GetCity, GetCurrentTime, ListCities]
}

/// Returns the name and coordinates of a city by ID.
@examples([
    {
        title: "Get Seattle"
        input: { cityId: "SEA" }
        output: {
            name: "Seattle"
            coordinates: { latitude: 47.6, longitude: -122.3 }
        }
    }
    {
        title: "Get Houston"
        input: { cityId: "HOU" }
        output: {
            name: "Houston"
            coordinates: { latitude: 29.8, longitude: -95.4 }
        }
    }
    {
        title: "Get unknown city"
        input: { cityId: "UNK" }
        error: {
            shapeId: "example.weather#NoSuchCity"
            content: { message: "no city with ID UNK" }
        }
    }
])
@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := {
        @required
        @httpLabel
        cityId: String
    }
    output := {
        @required
        name: String

        @required
        coordinates: CityCoordinates
    }

    errors: [NoSuchCity]
}

/// The requested city does not exist.
@error("client")
@httpError(404)
structure NoSuchCity {
    @required
    message: String
}

/// Returns the current server time in UTC.
@readonly
@http(method: "GET", uri: "/current-time")
operation GetCurrentTime {
    output := {
        @required
        time: Timestamp
    }
}

/// Returns a paginated list of cities.
@readonly
@paginated(inputToken: "nextToken", outputToken: "nextToken", items: "items")
@http(method: "GET", uri: "/cities")
operation ListCities {
    input := {
        @httpQuery("nextToken")
        nextToken: String
    }
    output := {
        nextToken: String

        @required
        items: CitySummaries
    }
}

/// The latitude and longitude of a city in decimal degrees.
structure CityCoordinates {
    @required
    latitude: Float

    @required
    longitude: Float
}

list CitySummaries {
    member: CitySummary
}

/// A brief summary of a city returned in list responses.
structure CitySummary {
    @required
    cityId: String

    @required
    name: String
}

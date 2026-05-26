$version: "2"

namespace example.weather

use aws.protocols#restJson1

/// Provides weather forecasts.
///
/// The service exposes city resources, each with a forecast sub-resource that
/// gives the current chance of rain. Cities are listed with pagination support.
@restJson1
@paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
service Weather {
    version: "2006-03-01"
    resources: [City]
    operations: [GetCurrentTime]
}

/// A city with a geographic location and an associated weather forecast.
resource City {
    identifiers: { cityId: CityId }
    properties: { coordinates: CityCoordinates }
    read: GetCity
    list: ListCities
    resources: [Forecast]
}

/// The weather forecast for a city, expressed as a chance of rain.
resource Forecast {
    identifiers: { cityId: CityId }
    properties: { chanceOfRain: Float }
    read: GetForecast
}

/// A unique identifier for a city. Alphanumeric characters and spaces only.
@pattern("^[A-Za-z0-9 ]+$")
string CityId

/// Returns the current server time in UTC.
@readonly
@http(method: "GET", uri: "/current-time")
operation GetCurrentTime {
    output := {
        @required
        time: Timestamp
    }
}

/// Returns the name and coordinates of a city by ID.
@readonly
@http(method: "GET", uri: "/cities/{cityId}")
operation GetCity {
    input := for City {
        @required
        @httpLabel
        $cityId
    }
    output := for City {
        @required
        @notProperty
        name: String

        @required
        $coordinates
    }
    errors: [NoSuchResource]
}

/// Returns a paginated list of cities.
///
/// Use `nextToken` from the response to fetch the next page, and `pageSize`
/// to control how many results are returned per page.
@readonly
@paginated(items: "items")
@http(method: "GET", uri: "/cities")
operation ListCities {
    input := {
        @httpQuery("nextToken")
        nextToken: String

        @httpQuery("pageSize")
        pageSize: Integer
    }
    output := {
        nextToken: String

        @required
        items: CitySummaries
    }
}

/// Returns the weather forecast for a city.
@readonly
@http(method: "GET", uri: "/cities/{cityId}/forecast")
operation GetForecast {
    input := for Forecast {
        @required
        @httpLabel
        $cityId
    }
    output := for Forecast {
        /// Probability of rain, between 0.0 (no rain) and 1.0 (certain rain).
        $chanceOfRain
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
@references([{resource: City}])
structure CitySummary {
    @required
    cityId: CityId

    @required
    name: String
}

/// Returned when a requested resource does not exist.
@error("client")
structure NoSuchResource {
    /// The type of resource that was not found (e.g. `"City"`).
    @required
    resourceType: String
}

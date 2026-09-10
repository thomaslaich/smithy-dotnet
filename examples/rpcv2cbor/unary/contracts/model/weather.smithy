$version: "2"

namespace example.weather

use smithy.protocols#rpcv2Cbor

/// Provides weather forecasts.
///
/// The same Weather service as the restjson1 example, served over the
/// rpcv2Cbor protocol: operations are invoked by name via POST with CBOR
/// bodies, so the model carries no HTTP binding traits.
@rpcv2Cbor
@paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
service Weather {
    version: "2006-03-01"
    resources: [City]
    operations: [GetCurrentTime, GetFlakyForecast]
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
operation GetCurrentTime {
    output := {
        @required
        time: Timestamp
    }
}

/// Returns the name and coordinates of a city by ID.
@readonly
operation GetCity {
    input := for City {
        @required
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
operation ListCities {
    input := {
        nextToken: String

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
operation GetForecast {
    input := for Forecast {
        @required
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

/// Returns the weather forecast for a city from an unreliable backend.
///
/// The server fails transiently on a schedule, returning the retryable
/// `ServiceUnavailable` error. With a retry strategy configured, the generated
/// client retries these failures automatically. This operation exists to
/// demonstrate retries.
@readonly
operation GetFlakyForecast {
    input := {
        @required
        cityId: CityId
    }
    output := {
        /// Probability of rain, between 0.0 (no rain) and 1.0 (certain rain).
        chanceOfRain: Float
    }
    errors: [ServiceUnavailable]
}

/// Returned when a requested resource does not exist.
@error("client")
structure NoSuchResource {
    /// The type of resource that was not found (e.g. `"City"`).
    @required
    resourceType: String
}

/// Returned when the service is temporarily unable to serve the request.
/// Marked `@retryable`, so retry strategies treat it as transient.
@error("server")
@retryable
structure ServiceUnavailable {
    message: String
}

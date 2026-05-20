$version: "2"

namespace example.weather

use alloy#simpleRestJson

/// Provides weather forecasts.
@simpleRestJson
@paginated(inputToken: "nextToken", outputToken: "nextToken", pageSize: "pageSize")
service Weather {
    version: "2006-03-01"
    resources: [City]
    operations: [GetCurrentTime]
}

resource City {
    identifiers: { cityId: CityId }
    properties: { coordinates: CityCoordinates }
    read: GetCity
    list: ListCities
    resources: [Forecast]
}

resource Forecast {
    identifiers: { cityId: CityId }
    properties: { chanceOfRain: Float }
    read: GetForecast
}

@pattern("^[A-Za-z0-9 ]+$")
string CityId

@readonly
@http(method: "GET", uri: "/current-time")
operation GetCurrentTime {
    output := {
        @required
        time: Timestamp
    }
}

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

@readonly
@http(method: "GET", uri: "/cities/{cityId}/forecast")
operation GetForecast {
    input := for Forecast {
        @required
        @httpLabel
        $cityId
    }
    output := for Forecast {
        $chanceOfRain
    }
}

structure CityCoordinates {
    @required
    latitude: Float

    @required
    longitude: Float
}

list CitySummaries {
    member: CitySummary
}

@references([{resource: City}])
structure CitySummary {
    @required
    cityId: CityId

    @required
    name: String
}

@error("client")
structure NoSuchResource {
    @required
    resourceType: String
}

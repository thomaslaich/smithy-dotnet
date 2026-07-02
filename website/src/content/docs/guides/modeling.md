---
title: Modeling
description: Beyond a single operation — resources, pagination, and HTTP binding traits in a Smithy model.
---

The [protocol pages](/smithy-dotnet/protocols/overview/) each show one small
operation to keep the focus on the wire format. Real services model more:
resources, pagination, and additional HTTP bindings. This guide walks through
those patterns with a fuller model.

The example uses `@simpleRestJson`, but the modeling patterns — resources,
pagination, and the HTTP binding traits — apply to any HTTP protocol
(`simpleRestJson`, `restJson1`, `restXml`). The protocol trait only changes the
wire format; see [Client & Server Usage](/smithy-dotnet/protocols/usage/) for the
generated handler and client.

## A fuller model

Adapted from the [Smithy quickstart](https://smithy.io/2.0/quickstart.html), this
model demonstrates resources, pagination, errors, and common HTTP binding traits.

```smithy
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
```

## HTTP binding traits

For HTTP protocols, these traits control where each member lives in the request
or response. Members without an explicit binding go into the JSON (or XML) body.

| Trait | Binds member to |
| --- | --- |
| `@httpLabel` | URI path segment |
| `@httpQuery("key")` | query string parameter |
| `@httpHeader("name")` | request or response header |
| `@httpPayload` | raw request/response body |

RPC protocols (`awsJson1_1`, `rpcv2Cbor`) and gRPC do not use HTTP binding
traits — every member is carried in the body/message. See each
[protocol page](/smithy-dotnet/protocols/overview/) for its modeling rules.

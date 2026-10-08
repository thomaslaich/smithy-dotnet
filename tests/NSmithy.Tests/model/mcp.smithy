$version: "2"

namespace nsmithy.tests.mcp

use smithy.ai#prompts

/// Shapes the mcp tests exercise, generated so the tests use the same schemas a consumer would.
@prompts({
    weather_brief: {
        description: "Create a weather brief"
        template: "Call LookupWeather."
    }
})
service Fixtures {
    version: "1"
    operations: [
        LookupWeather
    ]
}

/// Looks up the weather for a place.
@readonly
operation LookupWeather {
    input := {
        /// Place to look up.
        @required
        @jsonName("place_name")
        @length(min: 2, max: 80)
        place: String
    }

    output := {
        @required
        summary: String
    }

    errors: [
        LookupFailure
    ]
}

@error("client")
@httpError(400)
structure LookupFailure {
    @required
    message: String
}

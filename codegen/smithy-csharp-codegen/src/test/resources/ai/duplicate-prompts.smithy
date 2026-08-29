$version: "2"

namespace example.weather

use smithy.ai#prompts

@prompts({
    weather_report: {
        description: "Create a weather report"
        template: "Create a weather report."
    }
})
service WeatherService {
    version: "2026-08-28"
    operations: [GetForecast]
}

@prompts({
    Weather_Report: {
        description: "Get a weather report"
        template: "Get a weather report."
    }
})
operation GetForecast {}

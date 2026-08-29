$version: "2"

namespace example.weather

use smithy.ai#prompts

@prompts({
    weather_brief: {
        description: "Create a concise weather brief"
        template: "Summarize the forecast for {{location}}."
        arguments: WeatherBriefArguments
        preferWhen: "The user wants a short weather summary"
    }
})
service WeatherService {
    version: "2026-08-28"
    operations: [GetForecast]
}

@readonly
@prompts({
    forecast_for_location: {
        description: "Get the forecast for one location"
        template: "Get the forecast for {{location}}."
        arguments: GetForecastInput
    }
})
operation GetForecast {
    input: GetForecastInput
    output: GetForecastOutput
}

structure WeatherBriefArguments {
    /// City, coordinates, or another recognizable location.
    @required
    location: String
}

structure GetForecastInput {
    @required
    location: String
}

structure GetForecastOutput {
    summary: String
}

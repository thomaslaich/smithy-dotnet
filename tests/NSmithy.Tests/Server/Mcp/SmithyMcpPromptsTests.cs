using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSmithy.Server;
using NSmithy.Server.Mcp;

namespace NSmithy.Tests.Server.Mcp;

public sealed class SmithyMcpPromptsTests
{
    [Fact]
    public async Task CreatesPromptMetadataAndRendersTemplate()
    {
        var prompt = Assert.Single(SmithyMcpPrompts.Create([WeatherBrief]));

        Assert.Equal("weather_brief", prompt.ProtocolPrompt.Name);
        Assert.Equal("Create a concise weather brief", prompt.ProtocolPrompt.Description);
        var arguments = prompt.ProtocolPrompt.Arguments!;
        Assert.Equal(2, arguments.Count);
        Assert.Equal("location", arguments[0].Name);
        Assert.Equal("City or coordinates.", arguments[0].Description);
        Assert.True(arguments[0].Required);
        Assert.Equal("style", arguments[1].Name);
        Assert.False(arguments[1].Required);

        var result = await InvokeAsync(
            prompt,
            new Dictionary<string, JsonElement>
            {
                ["location"] = JsonSerializer.SerializeToElement("Zurich"),
                ["style"] = JsonSerializer.SerializeToElement("three sentences"),
            }
        );

        Assert.Equal("Create a concise weather brief", result.Description);
        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        Assert.Equal(
            "Call GetForecast for Zurich and summarize it in three sentences."
                + "\n\nTool preference: The user wants a short weather summary",
            Assert.IsType<TextContentBlock>(message.Content).Text
        );
    }

    [Fact]
    public async Task ReplacesMissingOptionalArgumentsWithEmptyText()
    {
        var prompt = Assert.Single(SmithyMcpPrompts.Create([WeatherBrief]));

        var result = await InvokeAsync(
            prompt,
            new Dictionary<string, JsonElement>
            {
                ["location"] = JsonSerializer.SerializeToElement("Zurich"),
            }
        );

        Assert.StartsWith(
            "Call GetForecast for Zurich and summarize it in .",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RejectsMissingUnknownAndNonStringArguments()
    {
        var prompt = Assert.Single(SmithyMcpPrompts.Create([WeatherBrief]));

        var missing = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await InvokeAsync(prompt, null)
        );
        var unknown = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await InvokeAsync(
                prompt,
                new Dictionary<string, JsonElement>
                {
                    ["location"] = JsonSerializer.SerializeToElement("Zurich"),
                    ["extra"] = JsonSerializer.SerializeToElement("unused"),
                }
            )
        );
        var nonString = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await InvokeAsync(
                prompt,
                new Dictionary<string, JsonElement>
                {
                    ["location"] = JsonSerializer.SerializeToElement(42),
                }
            )
        );

        Assert.Equal(McpErrorCode.InvalidParams, missing.ErrorCode);
        Assert.Contains("requires argument 'location'", missing.Message, StringComparison.Ordinal);
        Assert.Contains(
            "does not declare argument 'extra'",
            unknown.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("must be a string", nonString.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPromptNamesThatDifferOnlyByCase()
    {
        var duplicate = new ServicePromptDefinition("Weather_Brief", "Duplicate", "Duplicate");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SmithyMcpPrompts.Create([WeatherBrief, duplicate])
        );

        Assert.Contains("without regard to case", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersPromptsWithTheOfficialMcpBuilder()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().WithSmithyPrompts([WeatherBrief]);

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetServices<McpServerPrompt>());
    }

    private static async Task<GetPromptResult> InvokeAsync(
        McpServerPrompt prompt,
        IDictionary<string, JsonElement>? arguments
    )
    {
        await using var server = McpServer.Create(
            new StreamServerTransport(new MemoryStream(), new MemoryStream()),
            new McpServerOptions()
        );
        var context = new RequestContext<GetPromptRequestParams>(
            server,
            new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.PromptsGet },
            new GetPromptRequestParams { Name = prompt.ProtocolPrompt.Name, Arguments = arguments }
        );
        return await prompt.GetAsync(context);
    }

    private static readonly ServicePromptDefinition WeatherBrief = new(
        "weather_brief",
        "Create a concise weather brief",
        "Call GetForecast for {{location}} and summarize it in {{style}}.",
        "The user wants a short weather summary",
        [
            new ServicePromptArgumentDefinition("location", "City or coordinates.", true),
            new ServicePromptArgumentDefinition("style", null, false),
        ]
    );
}

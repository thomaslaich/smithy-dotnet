module NSmithy.FSharp.Tests.McpTests

open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Example.Kitchen
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server
open NSmithy.Server.Mcp
open Xunit

/// Charges every order the dish's price per guest.
type private Kitchen() =
    interface IKitchenServiceHandler with
        member _.OrderAsync(input, _) =
            Task.FromResult
                { Receipt =
                    Receipt.Paid
                        { Amount = decimal input.Dish.Price * decimal input.Guests
                          Currency = "CHF" } }

        member _.PingAsync(_, _) = Task.FromResult(PingOutput())

/// The MCP tools and prompts an F# service definition registers.
let private mcp () =
    let services = ServiceCollection()
    services.AddKitchenServiceHandler<Kitchen>() |> ignore
    services.AddMcpServer().WithSmithyService<KitchenServiceDefinition>() |> ignore
    let provider = services.BuildServiceProvider()
    let options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value
    provider, List.ofSeq (nonNull options.ToolCollection), List.ofSeq (nonNull options.PromptCollection)

let private tool name (tools: McpServerTool list) =
    tools |> List.find (fun tool -> tool.ProtocolTool.Name = name)

let private server () =
    McpServer.Create(new StreamServerTransport(new MemoryStream(), new MemoryStream()), McpServerOptions())

let private arguments (json: string) =
    nonNull (JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)) :> IDictionary<string, JsonElement>

[<Fact>]
let ``An F# service definition registers its operations as MCP tools and its prompts`` () =
    let provider, tools, prompts = mcp ()
    use _ = provider

    Assert.Equal<string list>([ "Order"; "Ping" ], tools |> List.map _.ProtocolTool.Name |> List.sort)

    let prompt = Assert.Single(prompts).ProtocolPrompt
    Assert.Equal("order_for_table", prompt.Name)

    let order = tool "Order" tools
    Assert.Equal("Orders a dish for a table.", order.ProtocolTool.Description)

    let required =
        order.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray()
        |> Seq.map (fun name -> nonNull (name.GetString()))
        |> List.ofSeq

    Assert.Equal<string list>([ "dish"; "table" ], required)

[<Fact>]
let ``An MCP tool call reaches the F# handler`` () : Task =
    task {
        let provider, tools, _ = mcp ()
        use _ = provider
        let order = tool "Order" tools
        use server = server ()

        let context =
            RequestContext<CallToolRequestParams>(
                server,
                JsonRpcRequest(Id = RequestId(1L), Method = RequestMethods.ToolsCall),
                CallToolRequestParams(
                    Name = "Order",
                    Arguments =
                        arguments """{"table":"7","dish":{"name":"pizza","ingredients":["cheese"],"price":9.5}}"""
                ),
                Services = provider
            )

        let! result = order.InvokeAsync(context)

        Assert.NotEqual(Some true, Option.ofNullable result.IsError)

        let paid = result.StructuredContent.Value.GetProperty("receipt").GetProperty("paid")
        Assert.Equal(19M, paid.GetProperty("amount").GetDecimal())
    }

[<Fact>]
let ``An MCP prompt renders the model's template`` () : Task =
    task {
        let provider, _, prompts = mcp ()
        use _ = provider
        let prompt = Assert.Single(prompts)
        use server = server ()

        let context =
            RequestContext<GetPromptRequestParams>(
                server,
                JsonRpcRequest(Id = RequestId(1L), Method = RequestMethods.PromptsGet),
                GetPromptRequestParams(
                    Name = "order_for_table",
                    Arguments = arguments """{"table":"7","dish":"pizza"}"""
                )
            )

        let! result = prompt.GetAsync(context)

        let message = Assert.Single(result.Messages)
        Assert.Equal("Order pizza for table 7.", Assert.IsType<TextContentBlock>(message.Content).Text)
    }

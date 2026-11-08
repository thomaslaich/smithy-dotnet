module NSmithy.FSharp.Tests.ClientTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Example.Kitchen
open Xunit

/// Answers every request with the response respond returns, and records the requests.
type private StubHandler(respond: HttpRequestMessage -> string -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    member val Requests = ResizeArray<HttpRequestMessage * string>()

    override this.SendAsync(request, cancellationToken) =
        task {
            let! body =
                match request.Content with
                | null -> Task.FromResult ""
                | content -> content.ReadAsStringAsync(cancellationToken)

            this.Requests.Add((request, body))
            return respond request body
        }

let private json (status: HttpStatusCode) (body: string) =
    let response = new HttpResponseMessage(status)
    response.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    response

let private client (handler: StubHandler) =
    new KitchenClient(
        new HttpClient(handler),
        KitchenClientConfig(
            Endpoint = Uri "http://kitchen.test",
            Protocol = NSmithy.Protocols.RestJson.RestJson1Protocol()
        )
    )

let private order =
    { Table = "7"
      ClientToken = None
      Dish =
        { Name = "pizza"
          Ingredients = [ Ingredient.CHEESE ]
          Sides = None
          Price = 9.5f
          Spice = None
          Tags = None
          Extras = []
          Blob = None
          MadeAt = None
          Big = None
          Dec = None
          Offset = 0L
          Type = None
          ToStringValue = None }
      Notes = None
      Seating = Seating.BAR
      Guests = 2
      Tip = None }

[<Fact>]
let ``An operation sends its input over the HTTP bindings and reads its output`` () : Task =
    task {
        let handler =
            new StubHandler(fun _ _ -> json HttpStatusCode.OK """{"receipt":{"comped":{}}}""")

        use client = client handler

        let! result = client.OrderAsync order

        match result with
        | Ok output -> Assert.Equal(Receipt.Comped, output.Receipt)
        | Error error -> failwith $"Unexpected error: {error}"

        let request, body = Seq.exactlyOne handler.Requests
        Assert.Equal(HttpMethod.Post, request.Method)
        Assert.Equal("/tables/7/orders", (nonNull request.RequestUri).AbsolutePath)
        Assert.Contains("\"name\":\"pizza\"", body)
        Assert.Contains("\"seating\":\"bar\"", body)
        Assert.DoesNotContain("table", body)
    }

[<Fact>]
let ``A missing idempotency token is filled in, and a given one is kept`` () : Task =
    task {
        let handler =
            new StubHandler(fun _ _ -> json HttpStatusCode.OK """{"receipt":{"comped":{}}}""")

        use client = client handler

        let! _ = client.OrderAsync order
        let! _ = client.OrderAsync { order with ClientToken = Some "mine" }

        let token (request: HttpRequestMessage, _) =
            request.Headers.GetValues("x-client-token") |> Seq.exactlyOne

        Assert.True(Guid.TryParse(token handler.Requests[0]) |> fst)
        Assert.Equal("mine", token handler.Requests[1])
    }

[<Fact>]
let ``A modeled error is returned as its case`` () : Task =
    task {
        let handler =
            new StubHandler(fun _ _ ->
                let response = json HttpStatusCode.Conflict """{"message":"gone","item":"cheese"}"""
                response.Headers.Add("x-amzn-errortype", "OutOfStock")
                response)

        use client = client handler

        let! result = client.OrderAsync order

        match result with
        | Error(OrderError.OutOfStock error) ->
            Assert.Equal(Ingredient.CHEESE, error.Item)
            Assert.Equal(Some "gone", error.ErrorMessage)
        | other -> failwith $"Unexpected result: {other}"
    }

[<Fact>]
let ``A service-level error is the Service case of every operation`` () : Task =
    task {
        let handler =
            new StubHandler(fun _ _ ->
                let response = json HttpStatusCode.ServiceUnavailable """{"message":"closed"}"""
                response.Headers.Add("x-amzn-errortype", "KitchenClosed")
                response)

        use client = client handler

        let! result = client.OrderAsync order

        match result with
        | Error(OrderError.Service(KitchenError.KitchenClosed _)) -> ()
        | other -> failwith $"Unexpected result: {other}"
    }

[<Fact>]
let ``A failure the model does not declare stays an exception`` () : Task =
    task {
        let handler =
            new StubHandler(fun _ _ ->
                let response = json HttpStatusCode.InternalServerError "{}"
                response.Headers.Add("x-amzn-errortype", "KitchenOnFire")
                response)

        use client = client handler

        let! error = Assert.ThrowsAsync<NSmithy.Client.SmithyClientException>(fun () -> client.OrderAsync order :> Task)

        // Passing through the Result wrapper keeps where the failure came from.
        Assert.Contains("SmithyClientRuntime", error.StackTrace)
    }

[<Fact>]
let ``Cancelling an operation that returns a Result cancels its task`` () : Task =
    task {
        let handler = new StubHandler(fun _ _ -> json HttpStatusCode.OK "{}")
        use client = client handler
        use cancelled = new CancellationTokenSource()
        cancelled.Cancel()

        let! _ =
            Assert.ThrowsAnyAsync<OperationCanceledException>(fun () ->
                client.OrderAsync(order, cancelled.Token) :> Task)

        ()
    }

[<Fact>]
let ``The client works through its interface and honors cancellation`` () : Task =
    task {
        let handler = new StubHandler(fun _ _ -> json HttpStatusCode.OK """{}""")
        use client = client handler :> IKitchenClient

        let! _ = client.PingAsync()
        use cancelled = new CancellationTokenSource()
        cancelled.Cancel()

        let! _ = Assert.ThrowsAnyAsync<OperationCanceledException>(fun () -> client.PingAsync(cancelled.Token) :> Task)

        let request, _ = handler.Requests[0]
        Assert.Equal(HttpMethod.Get, request.Method)
        Assert.Equal("/ping", (nonNull request.RequestUri).AbsolutePath)
    }

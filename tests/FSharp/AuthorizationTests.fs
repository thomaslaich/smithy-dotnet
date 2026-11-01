module NSmithy.FSharp.Tests.AuthorizationTests

open System.Net
open System.Net.Http
open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open Example.Kitchen
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open NSmithy.Server.AspNetCore
open Xunit

type private Kitchen() =
    interface IKitchenServiceHandler with
        member _.OrderAsync(_, _) =
            Task.FromResult { Receipt = Receipt.Pending "soon" }

        member _.PingAsync(_, _) = Task.FromResult(PingOutput())

        member _.CloseTableAsync(_, _) = Task.FromResult(CloseTableOutput())

/// Authenticates any request that carries an X-Api-Key header.
type private ApiKeyHandler
    (options: IOptionsMonitor<AuthenticationSchemeOptions>, logger: ILoggerFactory, encoder: UrlEncoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        if this.Request.Headers.ContainsKey "X-Api-Key" then
            let identity = ClaimsIdentity([], "ApiKey")
            Task.FromResult(AuthenticateResult.Success(AuthenticationTicket(ClaimsPrincipal identity, "ApiKey")))
        else
            Task.FromResult(AuthenticateResult.NoResult())

/// Requires an authenticated user everywhere unless an endpoint allows anonymous access.
let private startServer () =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore

    builder.Services.AddAuthentication("ApiKey").AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>("ApiKey", null)
    |> ignore

    builder.Services.AddAuthorization(fun options ->
        options.FallbackPolicy <- AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    |> ignore

    builder.Services.AddKitchenServiceHandler<Kitchen>() |> ignore
    let app = builder.Build()
    app.UseAuthentication() |> ignore
    app.UseAuthorization() |> ignore

    app.MapKitchenService(KitchenServiceProtocols.RestJson1).WithModeledAuthorization()
    |> ignore

    app.StartAsync().Wait()
    app

let private send (app: WebApplication) (request: HttpRequestMessage) =
    task {
        use client = app.GetTestClient()
        use! response = client.SendAsync request
        return response.StatusCode
    }

let private closeTable (apiKey: string option) =
    let request = new HttpRequestMessage(HttpMethod.Post, "/tables/7/close")
    apiKey |> Option.iter (fun key -> request.Headers.Add("X-Api-Key", key))
    request

[<Fact>]
let ``An operation that requires an API key rejects a request without one`` () : Task =
    task {
        use app = startServer ()
        use request = closeTable None

        let! status = send app request

        Assert.Equal(HttpStatusCode.Unauthorized, status)
    }

[<Fact>]
let ``An operation that requires an API key serves a request with one`` () : Task =
    task {
        use app = startServer ()
        use request = closeTable (Some "staff")

        let! status = send app request

        Assert.True(int status < 300, $"Expected success, got {status}.")
    }

[<Fact>]
let ``An operation without modeled auth allows anonymous requests`` () : Task =
    task {
        use app = startServer ()
        use request = new HttpRequestMessage(HttpMethod.Get, "/ping")

        let! status = send app request

        Assert.Equal(HttpStatusCode.OK, status)
    }

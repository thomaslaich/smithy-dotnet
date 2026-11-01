using System.Security.Claims;
using System.Text.Encodings.Web;
using Alloy.Test;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NSmithy.Server.AspNetCore;
using NSmithy.Server.AspNetCore.Docs;

var builder = WebApplication.CreateBuilder(args);
builder
    .Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        null
    );
builder.Services.AddAuthorization();
builder.Services.AddPizzaAdminServiceHandler<PizzaHandler>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapSmithyDocs();

// Operations require the API key unless the model marks them @auth([]) or @optionalAuth.
app.MapPizzaAdminService().WithModeledAuthorization();
app.Run();

// Authenticates requests that carry the demo key in the header @httpApiKeyAuth names.
internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    private const string ApiKey = "nsmithy-demo-key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var key))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!string.Equals(key, ApiKey, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "demo")], SchemeName);
        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)
            )
        );
    }
}

internal sealed class PizzaHandler : IPizzaAdminServiceHandler
{
    private static readonly Dictionary<string, MenuItem> DefaultMenu = new()
    {
        ["margherita"] = new MenuItem(
            Food.FromPizza(
                new Pizza(PizzaBase.TOMATO, "Margherita", [Ingredient.TOMATO, Ingredient.CHEESE])
            ),
            8.50f
        ),
        ["pepperoni"] = new MenuItem(
            Food.FromPizza(
                new Pizza(
                    PizzaBase.TOMATO,
                    "Pepperoni",
                    [Ingredient.TOMATO, Ingredient.CHEESE, Ingredient.PEPPERONI]
                )
            ),
            10.00f
        ),
        ["caesar"] = new MenuItem(
            Food.FromSalad(new Salad([Ingredient.SALAD, Ingredient.CHEESE], "Caesar")),
            7.00f
        ),
    };

    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, MenuItem>> _menus = new();

    public Task<AddMenuItemOutput> AddMenuItemAsync(
        AddMenuItemInput input,
        CancellationToken cancellationToken = default
    )
    {
        lock (_lock)
        {
            if (!_menus.TryGetValue(input.Restaurant, out var items))
                _menus[input.Restaurant] = items = new(DefaultMenu);
            var itemId = Guid.NewGuid().ToString("N")[..8];
            items[itemId] = input.MenuItem;
            return Task.FromResult(new AddMenuItemOutput(DateTimeOffset.UtcNow, itemId));
        }
    }

    public Task<GetMenuOutput> GetMenuAsync(
        GetMenuInput input,
        CancellationToken cancellationToken = default
    )
    {
        lock (_lock)
        {
            if (!_menus.TryGetValue(input.Restaurant, out var items))
                items = DefaultMenu;
            return Task.FromResult(new GetMenuOutput(items));
        }
    }

    public Task<HealthOutput> HealthAsync(
        HealthInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new HealthOutput("ok"));

    public Task<AuthenticatedHealthOutput> AuthenticatedHealthAsync(
        AuthenticatedHealthInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new AuthenticatedHealthOutput("authenticated"));

    public Task<VersionOutput> VersionAsync(
        VersionInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new VersionOutput("1.0.0"));

    public Task<HeaderEndpointOutput> HeaderEndpointAsync(
        HeaderEndpointInput input,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new HeaderEndpointOutput(
                input.CapitalizedHeader,
                input.LowercaseHeader,
                input.MixedHeader,
                input.UppercaseHeader
            )
        );

    public Task<RoundTripOutput> RoundTripAsync(
        RoundTripInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new RoundTripOutput(input.Label, input.Body, input.Header, input.Query));

    public Task<GetEnumOutput> GetEnumAsync(
        GetEnumInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new GetEnumOutput(input.Aa.Value));

    public Task<GetIntEnumOutput> GetIntEnumAsync(
        GetIntEnumInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new GetIntEnumOutput(input.Aa));

    public Task<CustomCodeOutput> CustomCodeAsync(
        CustomCodeInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new CustomCodeOutput(input.Code));

    public Task<HttpPayloadWithDefaultOutput> HttpPayloadWithDefaultAsync(
        HttpPayloadWithDefaultInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new HttpPayloadWithDefaultOutput(input.Body));

    public Task<HttpPayloadRequiredWithDefaultOutput> HttpPayloadRequiredWithDefaultAsync(
        HttpPayloadRequiredWithDefaultInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new HttpPayloadRequiredWithDefaultOutput(input.Body));

    public Task<OpenUnionsOutput> OpenUnionsAsync(
        OpenUnionsInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new OpenUnionsOutput(input.Data));

    public Task<PrimitivesOutput> PrimitivesAsync(
        PrimitivesInput input,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            new PrimitivesOutput(
                input.Duration,
                input.LocalDate,
                input.LocalTime,
                input.OffsetDateTime,
                input.Uuid
            )
        );

    public Task<PreserveOrderOutput> PreserveOrderAsync(
        PreserveOrderInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new PreserveOrderOutput(input.Document, input.Map));
}

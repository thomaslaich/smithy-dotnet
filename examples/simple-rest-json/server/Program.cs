using Alloy.Test;
using NSmithy.AspNetCore.Docs;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPizzaAdminServiceHandler<PizzaHandler>();

var app = builder.Build();
app.MapSmithyDocs();
app.MapPizzaAdminServiceHttp();
app.Run();

internal sealed class PizzaHandler : IPizzaAdminServiceHandler
{
    private static readonly Dictionary<string, MenuItem> DefaultMenu = new()
    {
        ["margherita"] = new MenuItem(
            Food.FromPizza(
                new Pizza(
                    PizzaBase.TOMATO,
                    "Margherita",
                    new Ingredients([Ingredient.TOMATO, Ingredient.CHEESE])
                )
            ),
            8.50f
        ),
        ["pepperoni"] = new MenuItem(
            Food.FromPizza(
                new Pizza(
                    PizzaBase.TOMATO,
                    "Pepperoni",
                    new Ingredients([Ingredient.TOMATO, Ingredient.CHEESE, Ingredient.PEPPERONI])
                )
            ),
            10.00f
        ),
        ["caesar"] = new MenuItem(
            Food.FromSalad(
                new Salad(new Ingredients([Ingredient.SALAD, Ingredient.CHEESE]), "Caesar")
            ),
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
            return Task.FromResult(new GetMenuOutput(new Menu(items)));
        }
    }

    public Task<HealthOutput> HealthAsync(
        HealthInput input,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new HealthOutput("ok"));

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

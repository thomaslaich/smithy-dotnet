using Alloy.Test;
using NSmithy.Client;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";
const string ApiKey = "nsmithy-demo-key";

var client = new PizzaAdminServiceClient(
    new Uri(endpoint),
    new() { AuthSchemes = { new HttpApiKeyAuthScheme("X-Api-Key", ApiKey) } }
);

var health = await client.HealthAsync(new HealthInput());
Console.WriteLine($"Health: {health.Status}");

var authenticatedHealth = await client.AuthenticatedHealthAsync(new AuthenticatedHealthInput());
Console.WriteLine($"Authenticated health: {authenticatedHealth.Status}");

var version = await client.VersionAsync(new VersionInput());
Console.WriteLine($"Version: {version.Version}");

var menu = await client.GetMenuAsync(new GetMenuInput("napoli"));
Console.WriteLine($"Menu for napoli ({menu.Menu.Values.Count} items):");
foreach (var (name, item) in menu.Menu.Values)
    Console.WriteLine($"  {name}: ${item.Price:F2}");

var added = await client.AddMenuItemAsync(
    new AddMenuItemInput(
        new MenuItem(
            Food.FromPizza(
                new Pizza(
                    PizzaBase.TOMATO,
                    "Hawaii",
                    new Ingredients([Ingredient.TOMATO, Ingredient.CHEESE, Ingredient.PINEAPPLE])
                )
            ),
            12.50f
        ),
        "napoli"
    )
);
Console.WriteLine($"Added item: {added.ItemId} at {added.Added:u}");

menu = await client.GetMenuAsync(new GetMenuInput("napoli"));
Console.WriteLine($"Updated menu ({menu.Menu.Values.Count} items):");
foreach (var (name, item) in menu.Menu.Values)
    Console.WriteLine($"  {name}: ${item.Price:F2}");

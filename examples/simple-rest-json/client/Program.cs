using Alloy.Test;

var endpoint = args.Length > 0 ? args[0] : "http://localhost:5000";

var client = new PizzaAdminServiceClient(new Uri(endpoint));

var health = await client.HealthAsync(new HealthInput());
Console.WriteLine($"Health: {health.Status}");

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

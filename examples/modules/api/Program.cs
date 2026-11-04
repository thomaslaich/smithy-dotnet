using Example.Shop;
using NSmithy.Examples.Modules.Orders;
using NSmithy.Examples.Modules.Payments;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddShopService();

// Each module implements its own operation's handler interface.
builder.Services.AddSingleton<IPlaceOrderHandler, PlaceOrderHandler>();
builder.Services.AddSingleton<ICapturePaymentHandler, CapturePaymentHandler>();

var app = builder.Build();
app.MapShopService();
app.Run();

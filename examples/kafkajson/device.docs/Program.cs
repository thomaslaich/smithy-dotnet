using NSmithy.Server.AspNetCore.Docs;

// Serves the generated documentation for the StreetlightDevice contract:
//   /asyncapi.json  — the AsyncAPI 3.1 document (emitted + copied to wwwroot by
//                     SmithyGenerateAsyncApi)
//   /asyncapi       — the Scalar interactive reference
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapSmithyAsyncApi();

app.Run();

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Scalar.AspNetCore;

namespace NSmithy.Server.AspNetCore.Docs;

public static class SmithyDocsExtensions
{
    /// <summary>
    /// Serves the smithy-openapi generated <c>openapi.json</c> as a static file at
    /// <c>/openapi.json</c> and mounts the Scalar interactive API explorer at <c>/openapi</c>.
    /// </summary>
    public static WebApplication MapSmithyOpenApi(this WebApplication app)
    {
        app.UseStaticFiles();
        app.MapScalarApiReference(
            "/openapi",
            options =>
            {
                options.OpenApiRoutePattern = "/openapi.json";
            }
        );
        return app;
    }

    /// <summary>
    /// Serves a generated <c>asyncapi.json</c> as a static file at
    /// <c>/asyncapi.json</c> and mounts the Scalar interactive reference at <c>/asyncapi</c>.
    /// Pair with an AsyncAPI codegen extension such as NSmithy.Bote, which generates the document
    /// and copies it to <c>wwwroot/asyncapi.json</c>.
    /// </summary>
    public static WebApplication MapSmithyAsyncApi(this WebApplication app)
    {
        app.UseStaticFiles();
        app.MapScalarApiReference(
            "/asyncapi",
            options =>
            {
                options.OpenApiRoutePattern = "/asyncapi.json";
            }
        );
        return app;
    }

    /// <summary>
    /// Serves the smithy-docgen generated documentation at <c>/docs</c>.
    /// Redirects <c>/docs</c> to <c>/docs/index.html</c> so the Sphinx entry point
    /// is served without requiring an explicit filename in the URL.
    /// </summary>
    public static WebApplication MapSmithyDocs(this WebApplication app)
    {
        app.UseStaticFiles();
        app.MapGet("/docs", () => Results.Redirect("/docs/index.html"));
        return app;
    }
}

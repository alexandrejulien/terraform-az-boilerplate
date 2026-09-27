using System.Security.Cryptography;
using System.Text;

namespace TfStudio.Server.Infrastructure;

public static class SecurityMiddleware
{
    public const string TokenHeader = "X-TfStudio-Token";

    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next(context);
        });

    /// <summary>
    /// Every /api call must carry the per-launch token in a custom header. Any web page open in a
    /// regular browser can reach 127.0.0.1, but it can neither read the token nor send a custom
    /// header cross-origin without a CORS preflight (which this server never approves), so it
    /// cannot trigger a plan/apply/destroy. The static UI files themselves are public.
    /// </summary>
    public static IApplicationBuilder UseApiToken(this IApplicationBuilder app, string token)
    {
        var expected = Encoding.UTF8.GetBytes(token);
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && !IsAuthorized(context.Request, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
    }

    private static bool IsAuthorized(HttpRequest request, byte[] expected)
    {
        var provided = request.Headers[TokenHeader].ToString();
        return provided.Length > 0
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), expected);
    }
}

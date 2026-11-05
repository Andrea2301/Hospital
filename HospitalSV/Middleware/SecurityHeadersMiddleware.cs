using HospitalSV.Services;
using Microsoft.Extensions.Options;

namespace HospitalSV.Middleware;

/// <summary>
/// Adds industry-standard security headers recommended by OWASP
/// to harden the API against clickjacking, sniffing, XSS, and information leakage.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DemoOptions _demo;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<DemoOptions> demo)
    {
        _next = next;
        _demo = demo.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Prevent MIME type sniffing
        headers["X-Content-Type-Options"] = "nosniff";

        // Prevent framing / clickjacking attacks
        headers["X-Frame-Options"] = "DENY";

        // Browser XSS protection filter
        headers["X-XSS-Protection"] = "1; mode=block";

        // Strict referrer policy
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Restrict sensitive browser features
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        // Keep the public sandbox out of search engines.
        if (_demo.Enabled)
            headers["X-Robots-Tag"] = "noindex, nofollow";

        // Prevent leaking backend server info
        headers.Remove("Server");
        headers.Remove("X-Powered-By");

        await _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}

namespace EEMOCantilanSDS.Api.Middleware
{
    /// <summary>
    /// Adds baseline security response headers. Registered in the non-development pipeline only, so the
    /// dev Swagger UI (HTML + inline scripts) is unaffected. The API returns JSON only, so a locked-down
    /// <c>default-src 'none'</c> CSP is safe and, with <c>frame-ancestors 'none'</c> + X-Frame-Options,
    /// prevents the API from being framed or from loading any sub-resources.
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;

        public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            return _next(context);
        }
    }
}

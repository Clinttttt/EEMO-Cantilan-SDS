using EEMOCantilanSDS.Client;
using EEMOCantilanSDS.Client.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddClient(builder.Configuration);

var app = builder.Build();

// Baseline browser security headers for the portal. Emitted as the first middleware so every response
// carries them. HSTS is set explicitly (not just UseHsts) because TLS is terminated at Azure's proxy,
// so the app sees HTTP and the built-in HSTS middleware would skip it. CSP is intentionally NOT
// tightened beyond framing here — Blazor Server needs inline bootstrap + a WebSocket, so a strict
// default-src would break the app.
var isProd = !app.Environment.IsDevelopment();
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "SAMEORIGIN";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    if (isProd)
        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}



app.UseStaticFiles();

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapStaticAssets();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
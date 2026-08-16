using EEMOCantilanSDS.Client;
using EEMOCantilanSDS.Client.Components;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddClient(builder.Configuration);

// TLS terminates at Azure's front door, so the portal itself is reached over HTTP and every absolute URL it builds came out
// as http:// — including the redirect from the bare domain, which answered 302 to http://console.stalltrack.site/login. HSTS
// covered a returning browser and the http URL was upgraded anyway, but a first-time visitor made one plaintext request for
// the URL, and any future absolute link would have the same fault.
//
// Scoped as tightly as the platform allows, on the office's instruction:
//
//   • ONLY XForwardedProto is honoured. The client IP (XForwardedFor) and the host (XForwardedHost) are deliberately not,
//     because nothing here needs them: forwarding the host invites host-header spoofing, and forwarding the IP would change
//     what any future rate limiting sees.
//   • ForwardLimit 1 — there is exactly one proxy in front of this app, so a chain of forwarded values is not trusted.
//   • KnownNetworks and KnownProxies are cleared, which is what App Service requires: the front door's address is not fixed,
//     so it cannot be named. This is the part that cannot be tightened, and it means the header is accepted from any caller.
//     The consequence is bounded: the only thing a forged header can do here is make the app believe an already-arrived
//     request was HTTPS, which changes the scheme it writes into a redirect. It grants no access and reveals nothing, and the
//     app is only reachable through that same front door.
//
// Two consequences worth knowing, both checked before this shipped:
//
//   • The authentication cookie uses CookieSecurePolicy.SameAsRequest, so while the app believed it was serving HTTP the
//     session cookie was issued WITHOUT the Secure flag. It is now marked Secure, which is what it should always have been.
//     Sessions already in flight are unaffected — a browser keeps sending an existing cookie either way.
//   • UseHttpsRedirection stays inert, because no HTTPS port is configured. That matters: had it been active while the app
//     saw HTTP, it would have redirected every request to HTTPS, the front door would have forwarded HTTP again, and the two
//     would have looped.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// HSTS must be configured, not left to its default, and this was learned the hard way: honouring the forwarded protocol made
// UseHsts start emitting — it had been skipping every request while the app believed it was serving HTTP — and its default
// policy OVERWROTE the explicit header below with a weaker one. The live response went from
// "max-age=31536000; includeSubDomains" to "max-age=2592000", a year down to thirty days with subdomains dropped, purely as a
// side effect of fixing the scheme. Set here so both writers state the same policy and it cannot matter which runs last.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

// First in the pipeline, before anything reads the scheme — the security headers below and UseHttpsRedirection further down
// both do.
app.UseForwardedHeaders();

// Baseline browser security headers for the portal. Emitted early so every response carries them.
//
// HSTS is still set explicitly rather than relying on UseHsts alone. With the forwarded protocol now honoured above,
// UseHsts would work — but this header is the one thing that must not depend on the proxy sending a header correctly, so it
// is written unconditionally in production. Belt and braces, deliberately.
//
// CSP is intentionally NOT tightened beyond framing here — Blazor Server needs inline bootstrap + a WebSocket, so a strict
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
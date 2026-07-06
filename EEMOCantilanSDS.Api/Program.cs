using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Structured logging (Hardening §7 — observability): emit machine-parseable JSON
// to stdout in non-development so the App Service log stream / any log collector can
// query errors by property (TraceId, Method, Path). Uses the built-in JSON console
// formatter — no external logging dependency, works on the current App Service tier.
// Development keeps the default readable console output.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

builder.Services.AddApi(builder.Environment,builder.Configuration);
builder.Services.AddInfrastructureService(builder.Configuration);
builder.Services.AddApplicationService(builder.Configuration);
builder.ConfigureServices();



var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync();
    await MunicipalitySeeder.SeedAsync(context);
    await FacilitySeeder.SeedAsync(context);
    await FacilityRateSeeder.SeedAsync(context);
}

// Resolve the default municipality once for tenant scoping. Best-effort: the municipality query filter
// is a no-op until this is set, so a DB hiccup here cannot take the app down or hide data.
using (var tenantScope = app.Services.CreateScope())
{
    try
    {
        var db = tenantScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = tenantScope.ServiceProvider
            .GetRequiredService<EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor>();
        var defaultMunicipalityId = await db.Municipalities.IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        accessor.Set(defaultMunicipalityId);
    }
    catch
    {
        // Leave unresolved — the tenant filter stays a no-op until it can be resolved later.
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
}


app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Strict,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

app.UseRouting();

app.UseRateLimiter();

app.UseCors("AllowClient");

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Readiness probe: confirms the database is reachable (for load-balancer/monitoring readiness checks).
// Liveness (/health) stays dependency-free so a transient DB blip doesn't restart the app.
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
   .AllowAnonymous();

app.MapHub<EEMOCantilanSDS.Api.Hubs.OnlinePaymentHub>("/hubs/online-payments");
app.MapHub<EEMOCantilanSDS.Api.Hubs.PayorNotificationHub>("/hubs/payor");

app.Run();

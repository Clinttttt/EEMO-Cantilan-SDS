using EEMOCantilanSDS.Client;
using EEMOCantilanSDS.Client.Components;
using EEMOCantilanSDS.Client.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddClient(builder.Configuration);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseMiddleware<AuthCookieMiddleware>();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();
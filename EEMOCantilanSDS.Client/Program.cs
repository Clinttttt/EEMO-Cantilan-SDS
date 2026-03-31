using EEMOCantilanSDS.Client;
using EEMOCantilanSDS.Client.Components;

var builder = WebApplication.CreateBuilder(args);



builder.Services.AddClient();
var app = builder.Build();


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

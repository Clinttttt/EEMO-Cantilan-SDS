using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApi(builder.Configuration);
builder.Services.AddInfrastructureService(builder.Configuration);
builder.Services.AddApplicationService(builder.Configuration);
builder.ConfigureServices();


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactLocal", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "https://localhost:5173", "https://localhost:7167", "http://localhost:5198")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    await FacilitySeeder.SeedAsync(context);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Strict,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

app.UseRouting();


app.UseCors("AllowReactLocal");

app.UseMiddleware<ExceptionHandlingMIddleware>();

// Auth middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

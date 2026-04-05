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

app.UseRouting();

app.UseCors("AllowAll");

app.UseMiddleware<ExceptionHandlingMIddleware>();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApi(builder.Configuration);
builder.Services.AddInfrastructure();
builder.Services.AddApplication();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI();
}
app.UseHttpsRedirection();

app.UseRouting();   

app.UseCors("AllowAll");

app.UseMiddleware<ExceptionHandlingMIddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

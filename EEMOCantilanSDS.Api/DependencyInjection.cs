using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

namespace EEMOCantilanSDS.Api
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApi(this IServiceCollection service, IConfiguration iconfiguration)
        {

            service.AddHttpContextAccessor();
            service.AddAuthorization();
            service.AddControllers();
            service.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.WithOrigins("https://localhost:7167", "http://localhost:5198")
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });
            service.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(iconfiguration.GetConnectionString(""));
            });
            service.AddSwaggerGen(options =>
            {
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    In = ParameterLocation.Header,
                    Description = "Please enter a valid token",
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT"
                });
                options.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                         new string[] { }
                    }
                }); 
            });



            return service;
        }
    }
}

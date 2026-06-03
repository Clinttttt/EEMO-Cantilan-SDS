using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

namespace EEMOCantilanSDS.Api
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApi(this IServiceCollection service, IConfiguration iconfiguration)
        {

            service.AddHttpContextAccessor();
            service.AddAuthorization();
            service.AddControllers()
                   .AddJsonOptions(o =>
                   {
                       o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); 
                   });

            service.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.WithOrigins("http://localhost:5173", "https://localhost:5173")
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
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
                options.CustomSchemaIds(s => s.FullName);
                ; 
            });



            return service;
        }
    }
}

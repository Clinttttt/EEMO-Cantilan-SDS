using EEMOCantilanSDS.Api.Services;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

namespace EEMOCantilanSDS.Api
{
    public static class DependencyInjection
    {
      
        public static IServiceCollection AddApi(this IServiceCollection service, IWebHostEnvironment environment, IConfiguration configuration)
        {

            service.AddHttpContextAccessor();
            service.AddAuthorization();
            service.AddSignalR();
            service.AddScoped<IOnlinePaymentNotifier,SignalROnlinePaymentNotifier>();
            service.AddScoped<IPayorRealtimeNotifier,SignalRPayorRealtimeNotifier>();
            service.AddControllers()
                   .AddJsonOptions(o =>
                   {
                       o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); 
                   });


            service.AddCors(options =>
            {
                options.AddPolicy("AllowClient", builder =>
                {
                    var allowedOrigins = environment.IsDevelopment() 
                    ? configuration.GetSection("Developmemt_Cors:AllowedOriginsDevelopment").Get<string[]>()
                    : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

                    allowedOrigins ??= Array.Empty<string>();

                    builder.WithOrigins("allowedOrigins")
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                });
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

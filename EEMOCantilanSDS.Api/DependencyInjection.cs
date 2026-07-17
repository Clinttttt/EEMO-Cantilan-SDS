using EEMOCantilanSDS.Api.Services;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace EEMOCantilanSDS.Api
{
    public static class DependencyInjection
    {
      
        public static IServiceCollection AddApi(this IServiceCollection service, IWebHostEnvironment environment, IConfiguration configuration)
        {

            service.AddHttpContextAccessor();
            service.AddAuthorization(options =>
            {
                // Platform operator = a SuperAdmin of the DEFAULT (Cantilan) LGU. Whole-database operations
                // (backup, restore, DB-health) run over the shared database across every LGU, so once a
                // second LGU exists a per-LGU Head must never trigger them — restrict to the default-tenant
                // SuperAdmin. While Cantilan is the only LGU this is exactly its Head, so behavior is unchanged.
                options.AddPolicy("PlatformOperator", policy => policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole("SuperAdmin")
                    && ctx.User.FindFirst(AppClaimTypes.Municipality)?.Value == TenantConstants.DefaultTenantCode));
            });
            service.AddSignalR();
            service.AddScoped<SignalROnlinePaymentNotifier>();
            service.AddScoped<CollectorPushOnlinePaymentNotifier>();
            service.AddScoped<IOnlinePaymentNotifier, CompositeOnlinePaymentNotifier>();
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
                    ? configuration.GetSection("Development_Cors:AllowedOriginsDevelopment").Get<string[]>()
                    : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

                    allowedOrigins ??= Array.Empty<string>();

                    builder.WithOrigins(allowedOrigins)
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                });
            });

            // Lenient per-client rate limit for the auth endpoints (login / refresh / activate): a coarse
            // brute-force safety net layered on the per-account 5-attempt lockout. Partitioned by the real
            // client IP — behind Azure App Service that arrives in X-Forwarded-For, so one shared upstream
            // proxy IP never throttles everyone globally, and normal human login rates are never affected.
            service.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy("auth", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ClientIpKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
                options.OnRejected = async (context, token) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)retryAfter.TotalSeconds).ToString();
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new { message = "Too many attempts. Please wait a minute and try again." }, token);
                };
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

        // Real client IP for rate-limit partitioning. Behind Azure App Service the client IP arrives in
        // X-Forwarded-For as "ip:port, proxy…"; fall back to the connection IP for local/dev.
        private static string ClientIpKey(HttpContext context)
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                var lastColon = first.LastIndexOf(':');
                // Strip ":port" from an IPv4 entry (dotted quad with a single colon); leave IPv6 intact.
                if (lastColon > 0 && first.IndexOf('.') >= 0 && first.IndexOf(':') == lastColon)
                    first = first[..lastColon];
                if (first.Length > 0) return first;
            }
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}

using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Extensions;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.HttpClients.ApiClients;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace EEMOCantilanSDS.Client
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddClient(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddRazorComponents()
                .AddInteractiveServerComponents();

            AddPersistence(service, configuration);

            service.AddHttpContextAccessor();

            // Single shared auth cookie. (A path-based dual-cookie scheme was tried but is unreliable in
            // Blazor Server: the interactive circuit runs over /_blazor, which has no area path, so the
            // selector mis-resolves. SameSite=Lax so the cookie survives the top-level redirect back from
            // the PayMongo hosted checkout to /payor/payment/success.)
            service.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name = ".AspNetCore.Cookies";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.ExpireTimeSpan = TimeSpan.FromDays(7);
                    options.SlidingExpiration = true;
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                    options.LogoutPath = "/api/authproxy/logout";

                    // Area-aware challenge: a single cookie scheme serves both the admin and payor
                    // areas, so the unauthenticated redirect must respect which area was requested —
                    // otherwise a logged-out payor visiting /payor is bounced to the ADMIN sign-in.
                    options.Events ??= new CookieAuthenticationEvents();
                    options.Events.OnRedirectToLogin = context =>
                    {
                        var loginPath = context.Request.Path.StartsWithSegments("/payor") ? "/payor/login" : "/login";
                        var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                        context.Response.Redirect($"{loginPath}?ReturnUrl={returnUrl}");
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        var loginPath = context.Request.Path.StartsWithSegments("/payor") ? "/payor/login" : "/login";
                        context.Response.Redirect(loginPath);
                        return Task.CompletedTask;
                    };
                });
            
            service.AddScoped<TokenService>();
            service.AddScoped<CircuitServicesAccessor>();
            service.AddScoped<SessionExpiredNotifier>();
            service.AddScoped<CircuitHandler, TokenCircuitHandler>();
            service.AddScoped<CircuitHandler, ServicesAccessorCircuitHandler>();
            service.AddScoped<AuthService>();
            service.AddScoped<PayorAuthService>();
            service.AddScoped<PayorRealtimeNotifier>();
            service.AddScoped<AuthorizationDelegatingHandler>();
            service.AddScoped<RefreshTokenDelegatingHandler>();
            service.AddScoped<AuthStateProvider>();

            service.AddScoped<AuthenticationStateProvider>(provider =>
                provider.GetRequiredService<AuthStateProvider>());

            service.AddControllers();

            return service;
        }

        public static IServiceCollection AddPersistence(this IServiceCollection service, IConfiguration configuration)
        {
            service.AddHttpClient<IAuthApiClient, AuthApiClient>("AuthClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });

            service.AddHttpClient("RefreshClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });

            service.AddHttpClient<IPayorAuthApiClient, PayorAuthApiClient>("PayorAuthClient", client =>
            {
                client.BaseAddress = new Uri(configuration["ApiBaseUrl"]!);
            });

            service.AddApiHttpClient<ISetupApiClient, SetupApiClient>(configuration);
            service.AddApiHttpClient<IStallsApiClient, StallsApiClient>(configuration);
            service.AddApiHttpClient<ICollectorsApiClient, CollectorsApiClient>(configuration);
            service.AddApiHttpClient<IAdminsApiClient, AdminsApiClient>(configuration);
            service.AddApiHttpClient<IPaymentsApiClient, PaymentsApiClient>(configuration);
            service.AddApiHttpClient<IVendorsApiClient, VendorsApiClient>(configuration);
            service.AddApiHttpClient<ITpmApiClient, TpmApiClient>(configuration);
            service.AddApiHttpClient<ITrmApiClient, TrmApiClient>(configuration);
            service.AddApiHttpClient<ISlaughterApiClient, SlaughterApiClient>(configuration);
            service.AddApiHttpClient<IDailyCollectionApiClient, DailyCollectionApiClient>(configuration);
            service.AddApiHttpClient<IFacilitiesApiClient, FacilitiesApiClient>(configuration);
            service.AddApiHttpClient<IDashboardApiClient, DashboardApiClient>(configuration);
            service.AddApiHttpClient<ITransactionsApiClient, TransactionsApiClient>(configuration);
            service.AddApiHttpClient<IPayorApiClient, PayorApiClient>(configuration);
            service.AddApiHttpClient<IOnlinePaymentsApiClient, OnlinePaymentsApiClient>(configuration);
            service.AddApiHttpClient<IAuditApiClient, AuditApiClient>(configuration);
            service.AddApiHttpClient<IReportsApiClient, ReportsApiClient>(configuration);

            return service;
        }
     

    }
}
